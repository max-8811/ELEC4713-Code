import re
import json
import os

# --- Configuration ---
STATS_FILES = {
    "Qwen-1.5B": "qwencoachstats.txt",
    "Llama3.2-3B-Q4_S": "3bscoachstats.txt",
    "Llama3.2-3B-Q4_M": "3bmcoachstats.txt",
    "Phi-3.5-3.8B": "phicoachstats.txt",
}
INPUT_PROMPTS_FILE = "100SquateInputPrompt.txt"
OUTPUT_FILE = "quality_assessment_results.txt"

# Keywords that indicate prescriptive language, based on SquatRecognizer.cs and observed outputs
PRESCRIPTIVE_KEYWORDS = [
    "widen stance", "bring heels in", "keep knees tracking", "straighten arms",
    "reach forward", "lower to at or below", "add a small hip hinge", "lift the chest",
    "brace so ribs stay stacked", "sit back slightly"
]

# --- Helper Functions ---

def parse_input_prompts(filename):
    """Parses the 100 original input prompts to get the ground truth for each."""
    if not os.path.exists(filename):
        print(f"Error: Input prompts file not found at '{filename}'")
        return []
    
    with open(filename, 'r', encoding='utf-8') as f:
        content = f.read()
    
    prompt_blocks = content.strip().split('\n\n')
    prompts_data = []
    for block in prompt_blocks:
        lines = block.strip().split('\n')
        if len(lines) < 2:
            continue
        
        try:
            json_data = json.loads(lines[0])
            # Handle potential multi-line issue paragraphs
            issue_paragraph = '\n'.join(lines[1:]).replace("Issue paragraph:", "").strip()
            prompts_data.append({
                "json_input": json_data,
                "issue_paragraph": issue_paragraph
            })
        except (json.JSONDecodeError, IndexError) as e:
            print(f"Warning: Could not parse prompt block: {block} -> {e}")

    return prompts_data

def parse_stats_file(filepath):
    """Parses a ...coachstats.txt file to extract all raw LLM JSON replies."""
    if not os.path.exists(filepath):
        print(f"Error: Stats file not found at '{filepath}'")
        return []
        
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()
        
    # Regex to find the JSON content of "Raw LLM Reply"
    raw_replies = re.findall(r"Raw LLM Reply \(JSON\):\s*(\{.*?\})\s*--", content, re.DOTALL)
    
    extracted_contents = []
    for reply_json_str in raw_replies:
        try:
            reply_data = json.loads(reply_json_str)
            assistant_content = reply_data.get("message", {}).get("content", "")
            extracted_contents.append(assistant_content)
        except json.JSONDecodeError:
            print(f"Warning: Could not decode JSON reply in {filepath}")
            extracted_contents.append(None) # Keep list length consistent
            
    return extracted_contents

def assess_quality(model_name, ground_truths, model_replies):
    """Runs the quality assessment for a single model's replies."""
    results = []
    # Skip the first "warm-up" prompt (index 0)
    for i in range(1, len(ground_truths)):
        if i >= len(model_replies):
            break 
            
        prompt_num = i + 1
        truth = ground_truths[i]
        reply_text = model_replies[i]
        
        score = 0
        checks = {
            "sentence_1_correct": False,
            "sentence_count_ok": False,
            "has_end_token": False,
            "no_prescriptive_lang": False,
            "is_factually_consistent": False
        }

        if reply_text is None or not reply_text.strip():
            results.append({"prompt_num": prompt_num, "score": 0, "checks": checks, "reply": "JSON DECODE ERROR OR EMPTY REPLY"})
            continue

        # Clean the reply text
        clean_reply = reply_text.strip()
        
        # 3. Check for termination token. Models might use variations.
        if "<END>" in clean_reply.upper() or clean_reply.upper().endswith("END") or clean_reply.upper().endswith("END."):
             checks["has_end_token"] = True
             score += 1
        
        # Remove any termination tokens for sentence analysis
        text_before_end = re.split(r'<END>', clean_reply, flags=re.IGNORECASE)[0].strip()
        if text_before_end.upper().endswith("END"):
             text_before_end = text_before_end[:-3].strip().rstrip('.')

        # Use a more robust regex to split sentences, handling multiple delimiters.
        sentences = [s.strip() for s in re.split(r'[.?!]\s*|\n', text_before_end) if s.strip()]

        # 1. Check Sentence 1 Correctness (FIXED LOGIC)
        expected_s1_base = f"{truth['json_input']['squatType']} with {truth['json_input']['bottomBias']}"
        if sentences:
            model_s1 = sentences[0]
            
            # Normalize for comparison: lowercase, remove punctuation, hyphens, and leading articles.
            def normalize_sentence(s):
                s = s.lower().strip().rstrip('.,;:')
                s = s.replace('-', ' ')
                if s.startswith("the "):
                    s = s[4:]
                return " ".join(s.split()) # Normalize whitespace

            normalized_model_s1 = normalize_sentence(model_s1)
            normalized_expected_s1 = normalize_sentence(expected_s1_base)

            # Check if the model's first sentence STARTS WITH the expected phrase.
            if normalized_model_s1.startswith(normalized_expected_s1):
                checks["sentence_1_correct"] = True
                score += 1

        # 2. Check Sentence Count
        if 2 <= len(sentences) <= 3:
            checks["sentence_count_ok"] = True
            score += 1

        # 4. Check for Prescriptive Language
        summary_text = " ".join(sentences[1:])
        if not any(keyword in summary_text.lower() for keyword in PRESCRIPTIVE_KEYWORDS):
            checks["no_prescriptive_lang"] = True
            score += 1
            
        # 5. Factual Consistency
        truth_issues = set(re.findall(r"(legs too wide|legs too narrow|trunk too upright|trunk too forward|arm not extended|arms not extended|left arm not extended|right arm not extended|arm too high|arms too high|left arm too high|right arm too high)", truth["issue_paragraph"]))
        model_issues = set(re.findall(r"(legs too wide|legs too narrow|trunk too upright|trunk too forward|arm not extended|arms not extended|left arm not extended|right arm not extended|arm too high|arms too high|left arm too high|right arm too high)", summary_text.lower()))

        if not truth_issues:
            # If there are no true issues, the model should not mention any.
            if not model_issues:
                checks["is_factually_consistent"] = True
                score += 1
        else:
            # If there are true issues, the model's summary must mention at least one of them,
            # and must not invent issues that weren't in the paragraph.
            if model_issues and model_issues.issubset(truth_issues):
                checks["is_factually_consistent"] = True
                score += 1
        
        results.append({
            "prompt_num": prompt_num,
            "score": score,
            "checks": checks,
            "reply": reply_text
        })
        
    return results

# --- Main Execution ---

def main():
    """Main function to run the full quality assessment and write the report."""
    ground_truths = parse_input_prompts(INPUT_PROMPTS_FILE)
    if not ground_truths:
        return

    with open(OUTPUT_FILE, 'w', encoding='utf-8') as f:
        f.write("--- LLM Quality Assessment Results ---\n\n")
        
        overall_scores = {}

        for model_name, stats_file in STATS_FILES.items():
            f.write(f"=========================================\n")
            f.write(f"Model: {model_name}\n")
            f.write(f"=========================================\n\n")
            
            model_replies = parse_stats_file(stats_file)
            assessment_results = assess_quality(model_name, ground_truths, model_replies)
            
            if not assessment_results:
                f.write("No results to assess.\n\n")
                continue

            total_score = sum(r['score'] for r in assessment_results)
            num_assessed = len(assessment_results)
            average_score = total_score / num_assessed if num_assessed > 0 else 0
            
            overall_scores[model_name] = average_score

            f.write(f"Average Quality Score: {average_score:.2f} / 5.00\n\n")
            
            # --- Per-Check Pass Rate Summary ---
            f.write("--- Pass Rate per Criterion ---\n")
            if not assessment_results:
                f.write("No results to calculate pass rates.\n")
            else:
                check_counts = {key: 0 for key in assessment_results[0]['checks'].keys()}
                for result in assessment_results:
                    for check, passed in result['checks'].items():
                        if passed:
                            check_counts[check] += 1
                for check, count in check_counts.items():
                    pass_rate = (count / num_assessed) * 100
                    f.write(f"- {check}: {pass_rate:.1f}%\n")
            
            f.write("\n--- Detailed Breakdown ---\n")
            # ------------------------------------
            
            for result in assessment_results:
                f.write(f"\n--- Prompt #{result['prompt_num']} ---\n")
                f.write(f"Score: {result['score']}/5\n")
                f.write(f"Reply: {result['reply'].strip()}\n")
                f.write("Checks:\n")
                for check, passed in result['checks'].items():
                    status = "PASS" if passed else "FAIL"
                    f.write(f"  - {check}: {status}\n")
            
            f.write("\n\n")

        f.write("=========================================\n")
        f.write("           FINAL SUMMARY\n")
        f.write("=========================================\n\n")
        for model_name, avg_score in sorted(overall_scores.items(), key=lambda item: item[1], reverse=True):
            f.write(f"{model_name}: {avg_score:.2f} / 5.00\n")
        
    print(f"Quality assessment complete. Results saved to '{OUTPUT_FILE}'.")

if __name__ == "__main__":
    main()