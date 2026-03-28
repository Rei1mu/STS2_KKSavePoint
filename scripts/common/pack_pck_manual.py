#!/usr/bin/env python3
"""手动创建PCK文件，不依赖Godot CLI"""

import struct
import json
import os
from pathlib import Path

def create_pck(pck_path: str, manifest_path: str, image_path: str = None):
    """创建一个简单的PCK文件"""
    
    # PCK文件头
    PCK_MAGIC = b'GDPC'
    PCK_VERSION = 3
    PCK_FORMAT_VERSION = 4
    
    files = []
    
    # 读取manifest并获取pck_name
    import json
    with open(manifest_path, 'r', encoding='utf-8') as f:
        manifest_data = json.load(f)
        pck_name = manifest_data.get('id', 'KKSavePoint')
    
    # 不添加manifest到PCK（与ModTemplate一致）
    # with open(manifest_path, 'rb') as f:
    #     manifest_data = f.read()
    # files.append((b'res://mod_manifest.json', manifest_data))
    
    # 读取图片
    if image_path and os.path.exists(image_path):
        # 读取原始PNG文件
        with open(image_path, 'rb') as f:
            image_data = f.read()
        files.append((b'res://mod_image.png', image_data))
        
        # 添加命名空间版本的图片（与原版一致）
        namespace_image_path = f"res://{pck_name}/mod_image.png".encode('utf-8')
        files.append((namespace_image_path, image_data))
        
        # 添加.import文件
        import_path = os.path.join(os.path.dirname(image_path), "mod_image.png.import")
        if os.path.exists(import_path):
            with open(import_path, 'rb') as f:
                import_data = f.read()
            namespace_import_path = f"res://{pck_name}/mod_image.png.import".encode('utf-8')
            files.append((namespace_import_path, import_data))
        
        # 添加.ctex文件（Godot导入后的纹理文件）
        ctex_path = os.path.join(os.path.dirname(image_path), ".godot", "imported")
        # 查找.ctex文件
        if os.path.exists(ctex_path):
            import glob
            ctex_files = glob.glob(os.path.join(ctex_path, "*.ctex"))
            if ctex_files:
                # 使用第一个找到的.ctex文件
                ctex_file = ctex_files[0]
                with open(ctex_file, 'rb') as f:
                    ctex_data = f.read()
                namespace_ctex_path = f"res://{pck_name}/mod_image.ctex".encode('utf-8')
                files.append((namespace_ctex_path, ctex_data))
    
    # 计算文件表偏移
    header_size = 4 + 4 + 4 + 4  # magic + version + format_version + reserved
    file_count = len(files)
    
    # 计算文件表大小
    file_table_size = 4  # file_count
    for path, data in files:
        file_table_size += 4 + len(path) + 1  # path_len + path + null
        file_table_size += 8 + 8  # offset + size
    
    # 数据从文件表之后开始
    data_offset = header_size + file_table_size
    
    with open(pck_path, 'wb') as f:
        # 写入文件头
        f.write(PCK_MAGIC)
        f.write(struct.pack('<I', PCK_VERSION))
        f.write(struct.pack('<I', PCK_FORMAT_VERSION))
        f.write(struct.pack('<I', 0))  # reserved
        
        # 写入文件数量
        f.write(struct.pack('<I', file_count))
        
        # 写入文件表
        current_offset = data_offset
        for path, data in files:
            f.write(struct.pack('<I', len(path) + 1))
            f.write(path)
            f.write(b'\x00')
            f.write(struct.pack('<Q', current_offset))
            f.write(struct.pack('<Q', len(data)))
            current_offset += len(data)
        
        # 写入文件数据
        for path, data in files:
            f.write(data)
    
    print(f"Created PCK: {pck_path}")
    print(f"  Files: {file_count}")
    print(f"  Size: {os.path.getsize(pck_path)} bytes")

if __name__ == '__main__':
    import sys
    
    project_root = Path(__file__).resolve().parent.parent.parent
    
    pck_path = sys.argv[1] if len(sys.argv) > 1 else str(project_root / 'KKSavePoint.pck')
    manifest_path = str(project_root / 'mod_manifest.json')
    image_path = str(project_root / 'mod_image.png')
    
    create_pck(pck_path, manifest_path, image_path)
