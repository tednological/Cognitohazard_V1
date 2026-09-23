extends RefCounted

## Where level files live, and how to find them.
##
## Shared by the editor's "L load next" and by the start screen's mission
## select. It exists because those two had begun to be the same directory scan
## written twice: a level that one could see and the other could not is the kind
## of difference nobody notices until a playtest loads the wrong floor.
##
## Everything here is static and file-system only. It never touches the bridge,
## the sim, or any live state -- it answers "what is on disk" and nothing else.

## Shipped levels, and levels the editor saved when res:// was read-only (which
## is every exported build).
const LEVELS_DIR: String = "res://levels"
const FALLBACK_DIR: String = "user://levels"


## Every level file, res:// first, then user://, each sorted. Deterministic, so
## the mission list does not reshuffle itself between launches.
static func list() -> PackedStringArray:
	var out := PackedStringArray()
	out.append_array(_scan(LEVELS_DIR))
	out.append_array(_scan(FALLBACK_DIR))
	return out


static func _scan(dir_path: String) -> PackedStringArray:
	var found := PackedStringArray()
	var dir := DirAccess.open(dir_path)
	if dir == null:
		return found
	for f in dir.get_files():
		if f.ends_with(".txt"):
			found.append(dir_path + "/" + f)
	found.sort()
	return found


static func read(path: String) -> String:
	return FileAccess.get_file_as_string(path)


## Index of `path` in `files`, or -1. Compared by FILENAME, so the exported
## res:// default still matches a copy the editor wrote to user://.
static func index_of(files: PackedStringArray, path: String) -> int:
	if path.is_empty():
		return -1
	var want: String = path.get_file()
	for i in range(files.size()):
		if files[i].get_file() == want:
			return i
	return -1
