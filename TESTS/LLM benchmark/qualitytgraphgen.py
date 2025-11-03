import re
import os
import pandas as pd
import matplotlib.pyplot as plt
import seaborn as sns

# --- Configuration ---
INPUT_FILE = "quality_assessment_results.txt"
OUTPUT_DIR = "quality_graphs"

def parse_quality_results(filepath):
    """Parses the quality assessment file to extract scores and pass rates."""
    if not os.path.exists(filepath):
        print(f"Error: File not found at {filepath}")
        return None

    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    # Regex to capture each model's block of results
    model_blocks = re.findall(r"Model: (.*?)\n=+\n\nAverage Quality Score: ([\d.]+).+?--- Pass Rate per Criterion ---\n(.*?)\n\n--- Detailed Breakdown ---", content, re.DOTALL)
    
    all_model_data = []

    for block in model_blocks:
        model_name = block[0].strip()
        avg_score = float(block[1])
        pass_rate_text = block[2]
        
        pass_rates = {}
        # Extract each criterion's pass rate
        for line in pass_rate_text.strip().split('\n'):
            match = re.match(r"- (.*?): ([\d.]+)%", line)
            if match:
                criterion = match.group(1).strip()
                rate = float(match.group(2))
                pass_rates[criterion] = rate
        
        all_model_data.append({
            "model": model_name,
            "average_score": avg_score,
            "pass_rates": pass_rates
        })
        
    return all_model_data

def create_quality_graphs():
    """Main function to load quality data and generate plots."""
    if not os.path.exists(OUTPUT_DIR):
        os.makedirs(OUTPUT_DIR)
        print(f"Created directory: {OUTPUT_DIR}")

    parsed_data = parse_quality_results(INPUT_FILE)
    if not parsed_data:
        print("No data parsed from the results file. Aborting.")
        return

    df = pd.DataFrame(parsed_data)

    # --- 1. Bar Chart: Average Quality Score ---
    plt.style.use('seaborn-v0_8-whitegrid')
    fig1, ax1 = plt.subplots(figsize=(12, 7))
    
    sns.barplot(x='model', y='average_score', data=df.sort_values('average_score', ascending=False), ax=ax1, palette='viridis')
    
    ax1.set_title('Average Quality Score per Model', fontsize=16, fontweight='bold')
    ax1.set_xlabel('Model', fontweight='bold')
    ax1.set_ylabel('Average Score (out of 5)')
    ax1.set_ylim(0, 5)

    # Add score labels on top of each bar
    for p in ax1.patches:
        ax1.annotate(f"{p.get_height():.2f}", (p.get_x() + p.get_width() / 2., p.get_height()),
                     ha='center', va='center', fontsize=12, color='black', xytext=(0, 5),
                     textcoords='offset points')

    plt.tight_layout()
    plt.savefig(os.path.join(OUTPUT_DIR, "1_average_quality_score.svg"))
    print("Saved: 1_average_quality_score.svg")
    plt.close(fig1)


    # --- 2. Heatmap: Pass Rate per Criterion ---
    # Prepare data for the heatmap
    heatmap_data = []
    for record in parsed_data:
        rates = record['pass_rates']
        rates['model'] = record['model']
        heatmap_data.append(rates)
        
    df_heatmap = pd.DataFrame(heatmap_data).set_index('model')
    
    fig2, ax2 = plt.subplots(figsize=(12, 8))
    sns.heatmap(df_heatmap, annot=True, fmt=".1f", cmap="YlGnBu", linewidths=.5, ax=ax2, cbar_kws={'label': 'Pass Rate (%)'})
    
    ax2.set_title('Model Pass Rate (%) per Quality Criterion', fontsize=16, fontweight='bold')
    ax2.set_xlabel('Quality Criterion', fontweight='bold')
    ax2.set_ylabel('Model', fontweight='bold')
    plt.xticks(rotation=15, ha="right")
    plt.yticks(rotation=0)
    
    plt.tight_layout()
    plt.savefig(os.path.join(OUTPUT_DIR, "2_criterion_pass_rate_heatmap.svg"))
    print("Saved: 2_criterion_pass_rate_heatmap.svg")
    plt.close(fig2)


if __name__ == "__main__":
    # Ensure you have the required libraries installed:
    # pip install pandas matplotlib seaborn
    create_quality_graphs()