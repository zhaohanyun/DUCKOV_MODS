# DUCKOV_MODS

《逃离鸭科夫》（Escape from Duckov）游戏 Mod 集合

## Mod 列表

### ScopeSensitivity - 更好的倍镜

**版本**: 1.0.0  
**作者**: 不明嚼栗  
**描述**: 倍镜优化 Mod

**功能**:
1. 优化近距离开镜后坐力，解决镜子飞出视野外的问题
2. 小幅减小整体后座力
3. 降低高倍镜开镜灵敏度，尤其是远距离

详见 [ScopeSensitivity/README.md](ScopeSensitivity/README.md)

## 说明

本仓库包含所有已开发和维护的 Duckov 游戏 Mod。每个 Mod 都是独立的子目录，包含完整的源代码、编译项目和说明文档。

## 安装

1. 下载对应 Mod 目录下的 `*.dll` 文件
2. 将文件复制到游戏 Mod 目录：`E:\Steam\steamapps\common\Escape from Duckov\Duckov_Data\Mods\[ModName]\`
3. 确保 `info.ini` 文件也在同一目录
4. 启动游戏

## 开发

各 Mod 使用 .NET SDK 开发，项目文件为 `*.csproj`。编译后会自动生成 DLL 文件在 `bin/Debug/netstandard2.1/` 目录下。

## 许可证

各 Mod 具体许可证请查看各自目录下的文件。

