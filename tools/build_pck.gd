extends SceneTree

# 在 .godot/imported 目录下查找与图片源文件名匹配的 .ctex 文件
func find_image_ctex(imported_dir: String, image_file_name: String) -> String:
	var dir := DirAccess.open(imported_dir)
	if dir == null:
		return ""
	var prefix := "%s-" % image_file_name
	for file in dir.get_files():
		if file.begins_with(prefix) and file.ends_with(".ctex"):
			return file
	return ""

# 生成 .import 文件的内容
func build_import_content(source_file: String, ctex_target: String) -> String:
	return """[remap]

importer="texture"
type="CompressedTexture2D"
path="%s"
metadata={
"vram_texture": false
}

[deps]

source_file="%s"
dest_files=["%s"]

[params]

compress/mode=0
compress/high_quality=false
compress/lossy_quality=0.7
compress/uastc_level=0
compress/rdo_quality_loss=0.0
compress/hdr_compression=1
compress/normal_map=0
compress/channel_pack=0
mipmaps/generate=false
mipmaps/limit=-1
roughness/mode=0
roughness/src_normal=""
process/channel_remap/red=0
process/channel_remap/green=1
process/channel_remap/blue=2
process/channel_remap/alpha=3
process/fix_alpha_border=true
process/premult_alpha=false
process/normal_map_invert_y=false
process/hdr_as_srgb=false
process/hdr_clamp_exposure=false
process/size_limit=0
detect_3d/compress_to=1
""" % [ctex_target, source_file, ctex_target]

# 为单个图片添加导入链（.ctex, .md5, .import）到 PCK
func add_image_import_chain(packer: PCKPacker, project_dir: String, image_file_name: String, image_target_path: String) -> int:
	var imported_dir := project_dir.path_join(".godot/imported")
	var ctex_name := find_image_ctex(imported_dir, image_file_name)
	if ctex_name.is_empty():
		push_error("找不到图片 '%s' 的导入文件（.ctex），请在编辑器中打开项目以确保图片已被导入。" % image_file_name)
		return ERR_FILE_NOT_FOUND
	
	# 添加 .ctex 文件
	var ctex_source := imported_dir.path_join(ctex_name)
	var ctex_target := "res://.godot/imported/%s" % ctex_name
	var add_ctex_ok := packer.add_file(ctex_target, ctex_source)
	if add_ctex_ok != OK:
		return add_ctex_ok
	
	# 添加 .md5 文件（如果存在）
	var md5_name := "%s.md5" % ctex_name
	var md5_source := imported_dir.path_join(md5_name)
	if FileAccess.file_exists(md5_source):
		var add_md5_ok := packer.add_file("res://.godot/imported/%s" % md5_name, md5_source)
		if add_md5_ok != OK:
			return add_md5_ok
	
	# 创建临时的 .import 文件并添加到 PCK
	var temp_import_local := "user://mod_image_runtime.import"
	var temp_import_global := ProjectSettings.globalize_path(temp_import_local)
	var file := FileAccess.open(temp_import_local, FileAccess.WRITE)
	if file == null:
		return ERR_CANT_CREATE
	file.store_string(build_import_content(image_target_path, ctex_target))
	file.close()
	var add_import_ok := packer.add_file("%s.import" % image_target_path, temp_import_global)
	if add_import_ok != OK:
		return add_import_ok
	DirAccess.remove_absolute(temp_import_global)
	return OK

# =========================================================================
# 递归扫描并打包 assets 文件夹（自动处理图片及其 import 依赖）
# =========================================================================
func pack_assets_recursive(packer: PCKPacker, project_dir: String, current_dir: String) -> int:
	var dir := DirAccess.open(current_dir)
	if dir == null:
		return OK # 目录不存在直接跳过

	dir.list_dir_begin()
	var file_name := dir.get_next()
	while file_name != "":
		if dir.current_is_dir():
			if file_name != "." and file_name != "..":
				# 递归进入子文件夹
				var err := pack_assets_recursive(packer, project_dir, current_dir.path_join(file_name))
				if err != OK: return err
		else:
			# 忽略现有的 .import 文件，因为打包脚本会为图片动态生成它们
			if not file_name.ends_with(".import"):
				var file_path := current_dir.path_join(file_name)
				
				# 1. 打包源文件本身
				var err := packer.add_file(file_path, file_path)
				if err != OK:
					push_error("打包资产文件失败: %s" % file_path)
					return err
				
				# 2. 如果是图片，额外处理 Godot 的导入链 (.ctex 等)
				var ext := file_name.get_extension().to_lower()
				if ext in ["png", "jpg", "jpeg", "webp", "svg"]:
					err = add_image_import_chain(packer, project_dir, file_name, file_path)
					if err != OK:
						push_error("处理图片导入链失败: %s" % file_path)
						return err
				
				print("已打包 Assets 资源: %s" % file_path)
		
		file_name = dir.get_next()
	
	return OK

# =========================================================================
# 递归扫描并打包翻译文件夹
# =========================================================================
func pack_localization_recursive(packer: PCKPacker, current_dir: String) -> int:
	var dir := DirAccess.open(current_dir)
	if dir == null:
		return OK # 如果目录不存在直接跳过，不阻断打包

	dir.list_dir_begin()
	var file_name := dir.get_next()
	while file_name != "":
		if dir.current_is_dir():
			if file_name != "." and file_name != "..":
				# 递归进入子文件夹
				var err := pack_localization_recursive(packer, current_dir.path_join(file_name))
				if err != OK: return err
		else:
			# 只提取 .json 文件
			if file_name.ends_with(".json"):
				var file_path := current_dir.path_join(file_name)
				var err := packer.add_file(file_path, file_path)
				if err != OK:
					push_error("打包翻译文件失败: %s" % file_path)
					return err
				print("已打包多语言资源: %s" % file_path)
		
		file_name = dir.get_next()
	
	return OK

func _initialize():
	# 定义输出路径
	var output_dir := "res://build"
	var output_file := "res://build/DirectConnectIP.pck"
	
	# 确保输出目录存在
	DirAccess.make_dir_recursive_absolute(output_dir)
	
	# 初始化 PCKPacker
	var packer := PCKPacker.new()
	var ok := packer.pck_start(output_file)
	if ok != OK:
		push_error("pck_start 失败: %s" % ok)
		quit(1)
	
	# 获取项目绝对路径（用于查找 .godot/imported）
	var project_dir := ProjectSettings.globalize_path("res://")
	
	# ========== 1. 添加 mod_manifest.json ==========
	var manifest_path := "res://mod_manifest.json"
	if not FileAccess.file_exists(manifest_path):
		push_error("找不到文件: %s" % manifest_path)
		quit(1)
	var add_manifest_ok := packer.add_file(manifest_path, manifest_path)
	if add_manifest_ok != OK:
		push_error("添加 mod_manifest.json 失败: %s" % add_manifest_ok)
		quit(1)
	
	# ========== 2. 动态扫描并添加 assets 目录下的所有资源 ==========
	var assets_dir := "res://assets"
	if DirAccess.dir_exists_absolute(assets_dir):
		var pack_assets_ok := pack_assets_recursive(packer, project_dir, assets_dir)
		if pack_assets_ok != OK:
			push_error("处理 Assets 文件打包失败: %s" % pack_assets_ok)
			quit(1)
	else:
		print("未检测到 assets 目录，跳过 assets 资源打包: %s" % assets_dir)
	
	# ========== 3. 添加 DirectConnectIP/mod_image.png ==========
	# (如果你的 mod_image.png 也移到了 assets 里面，这段其实可以删掉。为了安全我暂时保留)
	var mod_image_path := "res://DirectConnectIP/mod_image.png"
	if FileAccess.file_exists(mod_image_path):
		var add_mod_ok := packer.add_file(mod_image_path, mod_image_path)
		if add_mod_ok != OK:
			push_error("添加 mod_image.png 失败: %s" % add_mod_ok)
			quit(1)
		var mod_import_ok := add_image_import_chain(packer, project_dir, "mod_image.png", mod_image_path)
		if mod_import_ok != OK:
			push_error("处理 mod_image.png 导入链失败: %s" % mod_import_ok)
			quit(1)
	else:
		print("找不到文件 %s，跳过。" % mod_image_path)

	# ========== 4. 添加多语言翻译文件 ==========
	var loc_dir := "res://DirectConnectIP/localization"
	if DirAccess.dir_exists_absolute(loc_dir):
		var pack_loc_ok := pack_localization_recursive(packer, loc_dir)
		if pack_loc_ok != OK:
			push_error("处理多语言文件打包失败: %s" % pack_loc_ok)
			quit(1)
	else:
		print("未检测到多语言目录，跳过翻译文件打包: %s" % loc_dir)
	
	# 完成打包
	var flush_ok := packer.flush()
	if flush_ok != OK:
		push_error("flush 失败: %s" % flush_ok)
		quit(1)
	
	print(" PCK 生成成功: %s " % output_file)
	quit(0)