import re
import os
import pandas as pd
import matplotlib.pyplot as plt
import seaborn as sns

# --- Configuration ---
STATS_FILES = {
    "Qwen-1.5B": "qwencoachstats.txt",
    "Llama3.2-3B-Q4_S": "3bscoachstats.txt",
    "Llama3.2-3B-Q4_M": "3bmcoachstats.txt",
    "Phi-3.5-3.8B": "phicoachstats.txt",
}
OUTPUT_DIR = "latency_graphs"

def parse_stats_file(filepath):
    """Parses a single stats file to extract latency and token data for each prompt."""
    results = []
    try:
        with open(filepath, 'r', encoding='utf-8') as f:
            content = f.read()
            
            # Use regex to find all response time and tokens/sec entries
            times = re.findall(r"Response Time: ([\d.]+) seconds", content)
            tokens_ps = re.findall(r"Tokens per Second: ([\d.]+)", content)

            if len(times) == len(tokens_ps):
                for i in range(len(times)):
                    results.append({
                        'response_time_s': float(times[i]),
                        'tokens_per_second': float(tokens_ps[i])
                    })
            else:
                print(f"Warning: Mismatch in data points for {filepath}")

    except FileNotFoundError:
        print(f"Error: File not found at {filepath}")
    except Exception as e:
        print(f"An error occurred while parsing {filepath}: {e}")
        
    return results

def create_graphs():
    """Main function to load all data and generate the plots."""
    all_data = []
    
    # Check if output directory exists
    if not os.path.exists(OUTPUT_DIR):
        os.makedirs(OUTPUT_DIR)
        print(f"Created directory: {OUTPUT_DIR}")

    # Load data from all files
    for model_name, filename in STATS_FILES.items():
        model_results = parse_stats_file(filename)
        for result in model_results:
            result['model'] = model_name
            all_data.append(result)

    if not all_data:
        print("No data was loaded. Aborting graph generation.")
        return

    # Convert to pandas DataFrame for easier plotting
    df = pd.DataFrame(all_data)

    # --- 1. Bar Chart: Average Performance ---
    plt.style.use('seaborn-v0_8-whitegrid')
    fig1, ax1 = plt.subplots(figsize=(12, 7))
    
    # Group by model and calculate means
    avg_stats = df.groupby('model').mean().reset_index()
    
    # Set position of bar on X axis
    bar_width = 0.35
    r1 = range(len(avg_stats))
    r2 = [x + bar_width for x in r1]

    # Make the plot
    ax1.bar(r1, avg_stats['response_time_s'], color='#6495ED', width=bar_width, edgecolor='grey', label='Avg. Response Time (s)')
    ax1.set_ylabel('Average Response Time (seconds)', color='#6495ED')
    ax1.tick_params(axis='y', labelcolor='#6495ED')
    
    # Create a second y-axis for tokens/sec
    ax2 = ax1.twinx()
    ax2.bar(r2, avg_stats['tokens_per_second'], color='#FF7F50', width=bar_width, edgecolor='grey', label='Avg. Tokens / Second')
    ax2.set_ylabel('Average Tokens per Second', color='#FF7F50')
    ax2.tick_params(axis='y', labelcolor='#FF7F50')

    # Add xticks on the middle of the group bars
    plt.xlabel('Model', fontweight='bold')
    plt.xticks([r + bar_width/2 for r in range(len(avg_stats))], avg_stats['model'])
    
    ax1.set_title('Average Model Performance Comparison', fontsize=16, fontweight='bold')
    fig1.tight_layout()
    # Adding legends
    lines, labels = ax1.get_legend_handles_labels()
    lines2, labels2 = ax2.get_legend_handles_labels()
    ax2.legend(lines + lines2, labels + labels2, loc='upper left')
    
    plt.savefig(os.path.join(OUTPUT_DIR, "1_average_performance_bar_chart.svg"))
    print("Saved: 1_average_performance_bar_chart.png")
    plt.close(fig1)


    # --- 2. Box Plot: Response Time Distribution ---
    fig2, ax = plt.subplots(figsize=(12, 8))
    sns.boxplot(x='model', y='response_time_s', data=df, ax=ax, palette="coolwarm")
    ax.set_title('Distribution of Response Times per Model', fontsize=16, fontweight='bold')
    ax.set_xlabel('Model', fontweight='bold')
    ax.set_ylabel('Response Time (seconds)')
    ax.grid(True, which='both', linestyle='--', linewidth=0.5)
    plt.tight_layout()
    
    plt.savefig(os.path.join(OUTPUT_DIR, "2_response_time_distribution_box_plot.png"))
    print("Saved: 2_response_time_distribution_box_plot.png")
    plt.close(fig2)


    # --- 3. Scatter Plot: Tokens/sec vs. Response Time ---
    fig3, ax = plt.subplots(figsize=(12, 8))
    sns.scatterplot(
        data=df,
        x='response_time_s',
        y='tokens_per_second',
        hue='model',
        style='model',
        s=80, # size of points
        alpha=0.7,
        ax=ax
    )
    ax.set_title('Performance Profile: Tokens/Second vs. Response Time', fontsize=16, fontweight='bold')
    ax.set_xlabel('Response Time (seconds)', fontweight='bold')
    ax.set_ylabel('Tokens per Second', fontweight='bold')
    ax.legend(title='Model')
    ax.grid(True, which='both', linestyle='--', linewidth=0.5)
    plt.tight_layout()

    plt.savefig(os.path.join(OUTPUT_DIR, "3_performance_profile_scatter_plot.png"))
    print("Saved: 3_performance_profile_scatter_plot.png")
    plt.close(fig3)

if __name__ == "__main__":
    # Ensure you have the required libraries installed:
    # pip install pandas matplotlib seaborn
    create_graphs()