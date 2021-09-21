import glob
import json

tag_files = glob.glob("./data/minecraft/tags/**/*.json", recursive=True)

json_obj = {}

for tag_file in tag_files:
    element_name = tag_file.replace("./data/minecraft/tags\\", "").replace("\\", "/").replace(".json", "")
    base = element_name.split("/")

    if(len(base) == 3):
        tag_name = base[1] + "/" + base[2]
        tag_type = base[0]
    else:
        tag_type, tag_name = base

    print(f"Processing {element_name}")
    with open(tag_file) as tf:
        contents = json.load(tf)
        contents["name"] = tag_name
        contents["type"] = tag_type
        json_obj[element_name] = contents

results = json.dumps(json_obj, indent=2, sort_keys=True)

with open("tags.json", "w+") as f:
    f.write(results)