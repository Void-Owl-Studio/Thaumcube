class_name ContentPathResolver
extends RefCounted


static func to_resource_path(content_path: String) -> String:
	var normalized_path := content_path.strip_edges()
	if normalized_path.is_empty():
		return ""
	if normalized_path.begins_with("res://") or normalized_path.begins_with("user://"):
		return normalized_path
	if normalized_path.begins_with("/scripts/") or normalized_path.begins_with("/scenes/"):
		return "res:/" + normalized_path
	if normalized_path.begins_with("/textures/") or normalized_path.begins_with("/models/"):
		return "res://assets" + normalized_path
	if normalized_path.begins_with("/"):
		return "res://assets" + normalized_path
	return normalized_path
