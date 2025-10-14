import pandas as pd
import numpy as np
import os
import glob

# # Path to dataset
# d_path = 'G:/Games/Thesis+Survey+-+VR_August+26,+2025_09.57/'
# file_name = "Thesis Survey - VR_August 26, 2025_09.57.csv"
# df = pd.read_csv(os.path.join(d_path, file_name))

# # Background questions
# BG_COLS = {
#     "Q1": "cs_background",
#     "Q2": "nn_course",
#     "Q3_1": "derivative_familiarity"
# }

script_dir = os.path.dirname(os.path.abspath(__file__))

# Find CSV files, but exclude already coded outputs
csv_files = [f for f in glob.glob(os.path.join(script_dir, "*.csv"))
             if not os.path.basename(f).startswith("coded_")]

if not csv_files:
    raise FileNotFoundError("No valid raw CSV file found in the script folder.")
elif len(csv_files) > 1:
    raise RuntimeError("More than one CSV found. Please keep only one input CSV in the folder.")

csv_path = csv_files[0]

# Load dataset
df = pd.read_csv(csv_path)

# Detect group from filename
file_name = os.path.basename(csv_path)

BG_COLS = {
    "Q1": "cs_background",
    "Q2": "nn_course",
    "Q3_1": "derivative_familiarity"
}

# --- Detect group from file name ---
if "VR" in file_name:
    group = "VR"

    PRE_COLS = ["Q16","Q17","Q18","Q19","Q20","Q21","Q22","Q23","Q24","Q25","Q26","Q27"]
    POST_COLS = ["Q87","Q88","Q89","Q90","Q91","Q92","Q93","Q94","Q95","Q96","Q97","Q98"]

    ANSWER_KEY = {
        "Q16": "One entire pass over all training samples",
        "Q17": "6 4 3 1 5 2",
        "Q18": "A function measuring prediction error",
        "Q19": "Inputs are weighted and summed through the network",
        "Q20": "Inputs are weighted and summed through the network",
        "Q21": "Combining the outputs of previous neurons to generate the result of the network.",
        "Q22": "Determine the influence strength of an input on the neuron's output",
        "Q23": "Computes how errors change with respect to weights and biases",
        "Q24": "It provides feedback on how far predictions are from actual targets",
        "Q25": "2,3",
        "Q26": "To enable the activation function to shift left or right",
        "Q27": "Bias is learned during training like weights",
        "Q87": "One entire pass over all training samples",
        "Q88": "6 4 3 1 5 2",
        "Q89": "A function measuring prediction error",
        "Q90": "Inputs are weighted and summed through the network",
        "Q91": "Inputs are weighted and summed through the network",
        "Q92": "Combining the outputs of previous neurons to generate the result of the network.",
        "Q93": "Determine the influence strength of an input on the neuron's output",
        "Q94": "Computes how errors change with respect to weights and biases",
        "Q95": "It provides feedback on how far predictions are from actual targets",
        "Q96": "2,3",
        "Q97": "To enable the activation function to shift left or right",
        "Q98": "Bias is learned during training like weights"
    }

else:
    group = "Video"

    PRE_COLS = ["Q48","Q49","Q50","Q51","Q52","Q53","Q54","Q55","Q56","Q57","Q52.1","Q55.1"]
    POST_COLS = ["Q71","Q72","Q73","Q74","Q75","Q76","Q77","Q78","Q79","Q80","Q81","Q84"]

    ANSWER_KEY = {
        "Q48": "One entire pass over all training samples",
        "Q49": "6 4 3 1 5 2",
        "Q50": "A function measuring prediction error",
        "Q51": "Inputs are weighted and summed through the network",
        "Q52": "Inputs are weighted and summed through the network",
        "Q53": "Combining the outputs of previous neurons to generate the result of the network.",
        "Q54": "Determine the influence strength of an input on the neuron's output",
        "Q55": "Computes how errors change with respect to weights and biases",
        "Q56": "It provides feedback on how far predictions are from actual targets",
        "Q57": "2,3",
        "Q52.1": "To enable the activation function to shift left or right",
        "Q55.1": "Bias is learned during training like weights",
        "Q71": "One entire pass over all training samples",
        "Q72": "6 4 3 1 5 2",
        "Q73": "A function measuring prediction error",
        "Q74": "Inputs are weighted and summed through the network",
        "Q75": "Inputs are weighted and summed through the network",
        "Q76": "Combining the outputs of previous neurons to generate the result of the network.",
        "Q77": "Determine the influence strength of an input on the neuron's output",
        "Q78": "Computes how errors change with respect to weights and biases",
        "Q79": "It provides feedback on how far predictions are from actual targets",
        "Q80": "2,3",
        "Q81": "To enable the activation function to shift left or right",
        "Q84": "Bias is learned during training like weights"
    }

# --- Process participants ---
records = []

for idx, row in df.iterrows():
    rec = {"id": idx, "group": group}

    # Background info
    for col, newname in BG_COLS.items():
        if col in df.columns:
            rec[newname] = row[col]

    # Score pre-test
    pre_score = 0
    for col in PRE_COLS:
        if col in df.columns:
            answer = str(row[col]).strip()
            if answer not in ["nan", "NaN", ""]:
                rec[col] = int(answer == ANSWER_KEY[col])
                pre_score += rec[col]
    rec["pre_score"] = pre_score

    # Score post-test
    post_score = 0
    for col in POST_COLS:
        if col in df.columns:
            answer = str(row[col]).strip()
            if answer not in ["nan", "NaN", ""]:
                rec[col] = int(answer == ANSWER_KEY[col])
                post_score += rec[col]
    rec["post_score"] = post_score

    rec["gain"] = rec["post_score"] - rec["pre_score"]
    records.append(rec)

coded_df = pd.DataFrame(records)

# Save output safely in same folder
out_path = os.path.join(script_dir, "coded_results.csv")
coded_df.to_csv(out_path, index=False)

print(f"✅ Saved coded results to {out_path}")
print(coded_df.head())
