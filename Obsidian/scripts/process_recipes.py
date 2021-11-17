import glob
import json

recipe_files = glob.glob("./data/minecraft/recipes/*.json")

json_obj = {}

for recipe_file in recipe_files:
    recipe_name = recipe_file.replace("./data/minecraft/recipes\\", "").replace(".json", "")
    print(f"Processing {recipe_name}")
    with open(recipe_file) as rf:
        contents = json.load(rf)
        
        if "key" in contents:
            key_keys = contents["key"].keys()
            for key_key in key_keys:
                if (isinstance(contents["key"][key_key], list)):
                    for kk in contents["key"][key_key]:
                        kk["tag"] = kk["tag"] if "tag" in kk else None
                        kk["count"] = kk["count"] if "count" in kk else 1
                else:
                    contents["key"][key_key]["tag"] = contents["key"][key_key]["tag"] if "tag" in contents["key"][key_key] else None
                    contents["key"][key_key]["count"] = contents["key"][key_key]["count"] if "count" in contents["key"][key_key] else 1
        
        if "result" in contents and "item" in contents["result"]:
            contents["result"]["tag"] = contents["result"]["tag"] if "tag" in contents["result"] else None
            contents["result"]["count"] = contents["result"]["count"] if "count" in contents["result"] else 1
        
        if "ingredient" in contents:
            if (isinstance(contents["ingredient"], list)):
                for ing in contents["ingredient"]:
                    ing["tag"] = ing["tag"] if "tag" in ing else None
                    ing["count"] = ing["count"] if "count" in ing else 1
            else:
                contents["ingredient"]["tag"] = contents["ingredient"]["tag"] if "tag" in contents["ingredient"] else None
                contents["ingredient"]["count"] = contents["ingredient"]["count"] if "count" in contents["ingredient"] else 1
        json_obj[recipe_name] = contents

results = json.dumps(json_obj, indent=2, sort_keys=True)

with open("recipes.json", "w+") as f:
    f.write(results)