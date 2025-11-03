import requests
import json
import time
import os

# --- Configuration ---
MODEL_NAME = "testqwencoach"
INPUT_FILE = "100SquateInputPrompt.txt"
OUTPUT_FILE = "qwencoachstats.txt"
OLLAMA_URL = "http://localhost:11434/api/chat"

def parse_prompts(filename):
    """
    Parses the input file containing 100 squat prompts.
    Each prompt consists of a JSON line and an 'Issue paragraph:' line,
    separated by a blank line.
    """
    if not os.path.exists(filename):
        print(f"Error: Input file '{filename}' not found.")
        return []
        
    with open(filename, 'r', encoding='utf-8') as f:
        content = f.read()
    
    # Prompts are separated by two newline characters (a blank line)
    prompt_blocks = content.strip().split('\n\n')
    return prompt_blocks

def construct_full_prompt(prompt_block):
    """
    Constructs the full prompt string that mimics the C# SquatRecognizer logic.
    """
    lines = prompt_block.strip().split('\n')
    if len(lines) < 2:
        return None, None # Invalid block

    json_line = lines[0]
    issue_paragraph = "\n".join(lines[1:]) # Rejoin if paragraph has multiple lines

    try:
        summary_data = json.loads(json_line)
        squat_type = summary_data.get("squatType", "squat")
        bottom_bias = summary_data.get("bottomBias", "neutral bias")
    except json.JSONDecodeError:
        print(f"Warning: Could not parse JSON line: {json_line}")
        return None, None

    # This structure is based on BuildSummarizationPrompt in SquatRecognizer.cs
    expected_first_sentence = f"{squat_type} with {bottom_bias}."
    
    full_prompt = (
        "You are a coaching assistant summarizing a squat analysis.\n"
        "Follow these rules exactly:\n"
        f"1. Sentence 1 must be exactly: {expected_first_sentence}\n"
        "2. Write 1 to 2 additional sentences that concisely summarise the main issues described in the paragraph, using only the provided facts.\n"
        "3. If the paragraph states that no issues were present, emphasise consistent technique instead of inventing problems.\n"
        "4. Keep the entire summary to at most 3 sentences and end with <END>.\n"
        "5. Do not invent new details, avoid phase-by-phase lists, and do not include explicit action or prescription sentences.\n\n"
        "JSON:\n"
        f"{json_line}\n\n"
        f"{issue_paragraph}"
    )
    return full_prompt, prompt_block


def test_model():
    """
    Main function to run the benchmark. It reads prompts, sends them to the Ollama API,
    and records the results.
    """
    prompts = parse_prompts(INPUT_FILE)
    if not prompts:
        return

    results = []
    total_response_time = 0
    total_tokens_per_second = 0

    print(f"Starting benchmark for model '{MODEL_NAME}' with {len(prompts)} prompts...")

    for i, prompt_block in enumerate(prompts):
        full_prompt, original_block = construct_full_prompt(prompt_block)
        if not full_prompt:
            continue

        print(f"Processing prompt {i + 1}/{len(prompts)}...")

        payload = {
            "model": MODEL_NAME,
            "messages": [{"role": "user", "content": full_prompt}],
            "stream": False,
            "options": {
                "temperature": 0.05,
                "top_p": 0.9,
                "num_predict": 140,
                "repeat_penalty": 1.1
            }
        }

        try:
            start_time = time.perf_counter()
            response = requests.post(OLLAMA_URL, json=payload, timeout=150) # 150s timeout
            end_time = time.perf_counter()

            response.raise_for_status()  # Raise an exception for bad status codes (4xx or 5xx)
            
            response_data = response.json()
            raw_reply = response.text # Store the full raw JSON reply
            
            # Calculate metrics
            response_time = end_time - start_time
            eval_count = response_data.get('eval_count', 0)
            tokens_per_second = eval_count / response_time if response_time > 0 else 0
            
            # Accumulate totals
            total_response_time += response_time
            total_tokens_per_second += tokens_per_second
            
            results.append({
                "prompt_num": i + 1,
                "response_time_s": response_time,
                "tokens_per_second": tokens_per_second,
                "raw_reply": raw_reply
            })

        except requests.exceptions.RequestException as e:
            print(f"\n--- ERROR on prompt {i + 1} ---")
            print(f"Could not connect to Ollama or request failed: {e}")
            print("Please ensure the Ollama server is running and accessible.")
            print("--------------------------\n")
            results.append({
                "prompt_num": i + 1,
                "response_time_s": 0,
                "tokens_per_second": 0,
                "raw_reply": f"ERROR: {e}"
            })
        except json.JSONDecodeError:
            print(f"\n--- ERROR on prompt {i + 1} ---")
            print("Failed to decode JSON from Ollama response.")
            print("--------------------------\n")
            results.append({
                "prompt_num": i + 1,
                "response_time_s": 0,
                "tokens_per_second": 0,
                "raw_reply": "ERROR: Invalid JSON response from server."
            })


    # Write results to file
    with open(OUTPUT_FILE, 'w', encoding='utf-8') as f:
        f.write(f"--- Performance Stats for Model: {MODEL_NAME} ---\n\n")
        
        for result in results:
            f.write(f"--- Prompt #{result['prompt_num']} ---\n")
            f.write(f"Response Time: {result['response_time_s']:.4f} seconds\n")
            f.write(f"Tokens per Second: {result['tokens_per_second']:.2f}\n")
            f.write("Raw LLM Reply (JSON):\n")
            f.write(result['raw_reply'])
            f.write("\n\n--------------------------------------------------\n\n")
            
        # Calculate and write averages
        num_successful = len([r for r in results if r['response_time_s'] > 0])
        if num_successful > 0:
            avg_response_time = total_response_time / num_successful
            avg_tokens_per_second = total_tokens_per_second / num_successful
            
            f.write("--- Overall Averages ---\n")
            f.write(f"Total Prompts Processed: {len(results)}\n")
            f.write(f"Successful Responses: {num_successful}\n")
            f.write(f"Average Response Time: {avg_response_time:.4f} seconds\n")
            f.write(f"Average Tokens per Second: {avg_tokens_per_second:.2f}\n")
            f.write("------------------------\n")

    print(f"\nBenchmark complete. Results saved to '{OUTPUT_FILE}'.")

if __name__ == "__main__":
    test_model()