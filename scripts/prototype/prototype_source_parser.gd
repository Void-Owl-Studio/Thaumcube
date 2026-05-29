class_name PrototypeSourceParser
extends RefCounted

const PrototypeDefinitionScript := preload("res://scripts/prototype/prototype_definition.gd")


func parse_file(path: String) -> Array:
	var file := FileAccess.open(path, FileAccess.READ)
	if file == null:
		push_error("Cannot open prototype file '%s': error %s" % [path, FileAccess.get_open_error()])
		return []

	return parse_text(file.get_as_text(), path)


func parse_text(source: String, source_path: String = "<memory>") -> Array:
	var definitions: Array = []
	var current_definition = null
	var line_number := 0

	for raw_line in source.split("\n", false):
		line_number += 1
		var line := _strip_inline_comment(raw_line).strip_edges()
		if line.is_empty():
			continue

		if line.begins_with("/"):
			current_definition = PrototypeDefinitionScript.new(line, source_path, line_number)
			definitions.append(current_definition)
			continue

		if current_definition == null:
			push_error("%s:%s: var assignment without prototype path" % [source_path, line_number])
			continue

		var separator_index := line.find("=")
		if separator_index < 1:
			push_error("%s:%s: expected 'var = value'" % [source_path, line_number])
			continue

		var var_name := line.substr(0, separator_index).strip_edges()
		var raw_value := line.substr(separator_index + 1).strip_edges()
		current_definition.set_var(var_name, _parse_value(raw_value))

	return definitions


func _parse_value(raw_value: String):
	if raw_value.length() >= 2:
		var first_char := raw_value.substr(0, 1)
		var last_char := raw_value.substr(raw_value.length() - 1, 1)
		if (first_char == "\"" and last_char == "\"") or (first_char == "'" and last_char == "'"):
			return raw_value.substr(1, raw_value.length() - 2)

	var lower_value := raw_value.to_lower()
	if lower_value == "true":
		return true
	if lower_value == "false":
		return false
	if lower_value == "null":
		return null
	if raw_value.is_valid_int():
		return raw_value.to_int()
	if raw_value.is_valid_float():
		return raw_value.to_float()
	return raw_value


func _strip_inline_comment(raw_line: String) -> String:
	var in_string := false
	var string_delimiter := ""

	for char_index in raw_line.length():
		var current_char := raw_line.substr(char_index, 1)
		if in_string:
			if current_char == string_delimiter:
				in_string = false
			continue

		if current_char == "\"" or current_char == "'":
			in_string = true
			string_delimiter = current_char
			continue

		if current_char == "#":
			return raw_line.substr(0, char_index)

	return raw_line
