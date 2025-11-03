import pandas as pd
import numpy as np
import matplotlib.pyplot as plt

df = pd.read_csv("bigSideSquatData.csv")

src_cols = [
    "t_srcA","t_srcB","t_srcC",
    "lk_srcA","lk_srcB","lk_srcC",
    "rk_srcA","rk_srcB","rk_srcC"
]
for c in src_cols:
    df[c] = df[c].astype(str).str.strip()

df["any_inferred"]  = df[src_cols].apply(lambda r: int(any(v == "Inferred"  for v in r.values)), axis=1)
df["any_estimated"] = df[src_cols].apply(lambda r: int(any(v == "Estimated" for v in r.values)), axis=1)

phase_order = ["standing", "descending", "bottom", "ascending"]
df["phase"] = pd.Categorical(df["phase"], categories=phase_order, ordered=True)

# Bin frames
bin_size = 100
df["frame_bin"] = (df["frame"] // bin_size) * bin_size  # e.g., 0,100,200,...

# Aggregate: share per phase x frame_bin
agg = df.groupby(["phase", "frame_bin"]).agg(
    inferred_share=("any_inferred", "mean"),
    estimated_share=("any_estimated", "mean"),
    n=("frame", "count")
).reset_index()

# Pivot to matrices (rows=phase, cols=frame_bin)
inferred_piv = agg.pivot(index="phase", columns="frame_bin", values="inferred_share").reindex(index=phase_order)
estimated_piv = agg.pivot(index="phase", columns="frame_bin", values="estimated_share").reindex(index=phase_order)

# Plot side-by-side heatmaps
fig, axes = plt.subplots(1, 2, figsize=(14, 5), sharey=True)

im0 = axes[0].imshow(inferred_piv.values, aspect="auto", cmap="Reds", vmin=0, vmax=1, interpolation="nearest")
axes[0].set_title("Inferred share by phase and time (front view)")
axes[0].set_yticks(range(len(phase_order)))
axes[0].set_yticklabels(phase_order)
axes[0].set_xticks(range(len(inferred_piv.columns)))
axes[0].set_xticklabels(inferred_piv.columns, rotation=45, ha="right")
axes[0].set_xlabel(f"Frame bin (size={bin_size})")
axes[0].set_ylabel("Phase")

im1 = axes[1].imshow(estimated_piv.values, aspect="auto", cmap="Oranges", vmin=0, vmax=1, interpolation="nearest")
axes[1].set_title("Estimated share by phase and time (front view)")
axes[1].set_xticks(range(len(estimated_piv.columns)))
axes[1].set_xticklabels(estimated_piv.columns, rotation=45, ha="right")
axes[1].set_xlabel(f"Frame bin (size={bin_size})")

# Colorbars
cbar0 = fig.colorbar(im0, ax=axes[0], fraction=0.046, pad=0.04)
cbar0.set_label("Share (0..1)")
cbar1 = fig.colorbar(im1, ax=axes[1], fraction=0.046, pad=0.04)
cbar1.set_label("Share (0..1)")

plt.tight_layout()
plt.show()