#!/usr/bin/env python3
"""检查PCK文件内容"""

import struct
import os

def check_pck(pck_path: str):
    """检查PCK文件内容"""
    
    if not os.path.exists(pck_path):
        print(f"PCK文件不存在: {pck_path}")
        return
    
    with open(pck_path, 'rb') as f:
        # 读取文件头
        magic = f.read(4)
        if magic != b'GDPC':
            print(f"无效的PCK文件: {pck_path}")
            return
        
        version = struct.unpack('<I', f.read(4))[0]
        format_version = struct.unpack('<I', f.read(4))[0]
        reserved = struct.unpack('<I', f.read(4))[0]
        
        print(f"PCK文件: {pck_path}")
        print(f"版本: {version}, 格式: {format_version}")
        print(f"文件大小: {os.path.getsize(pck_path)} bytes")
        
        # 读取文件数量
        file_count = struct.unpack('<I', f.read(4))[0]
        print(f"\n包含文件数: {file_count}")
        
        # 读取文件表
        for i in range(file_count):
            path_len = struct.unpack('<I', f.read(4))[0]
            path = f.read(path_len - 1).decode('utf-8')
            f.read(1)  # null terminator
            
            offset = struct.unpack('<Q', f.read(8))[0]
            size = struct.unpack('<Q', f.read(8))[0]
            
            print(f"  {i+1}. {path} (offset: {offset}, size: {size} bytes)")

if __name__ == '__main__':
    import sys
    
    if len(sys.argv) > 1:
        pck_path = sys.argv[1]
    else:
        pck_path = "dist/KKSavePoint/KKSavePoint.pck"
    
    check_pck(pck_path)