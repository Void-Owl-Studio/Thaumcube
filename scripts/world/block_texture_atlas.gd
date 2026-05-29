class_name BlockTextureAtlas
extends RefCounted

var texture: Texture2D
var uv_rects: Dictionary = {}


func get_uv_rect(texture_path: String) -> Rect2:
	return uv_rects.get(texture_path, Rect2())
