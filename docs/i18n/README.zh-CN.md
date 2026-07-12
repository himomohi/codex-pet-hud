<div align="center">
  <img src="../images/hero.svg" alt="Codex Pet HUD：猫咪与两瓶用量药水" width="920">
  <h1>Codex Pet HUD</h1>
  <p><strong>两瓶药水，一只宠物，零干扰。</strong></p>
  <p>原生支持 macOS 与 Windows，在 Codex 宠物两侧以药水显示五小时和每周用量。</p>
  <p><a href="README.ko.md">한국어</a> · <a href="../../README.md">English</a> · <strong>简体中文</strong> · <a href="README.ja.md">日本語</a></p>
</div>

## 项目简介

宠物始终居中且可点击。左侧红色药水显示五小时用量，右侧蓝色药水显示每周用量。悬停可查看距离重置的剩余时间，点击可打开详情并手动刷新。

| 🐾 宠物联动 | ⚡ 双平台原生 | 🔒 安静且注重隐私 |
|---|---|---|
| 宠物隐藏时 HUD 同步隐藏，重新显示时自动恢复。 | macOS 使用 Swift/AppKit，Windows 使用 .NET 8/WPF。 | 不采集键盘输入、不记录令牌，也不会创建重复宠物。 |

## 核心功能

- 不遮挡宠物的暗黑风格左右药水
- 实时剩余用量、重置倒计时与 20/10/5% 阈值提醒
- 缩放、间距、居中定位与偏移设置
- macOS 菜单栏与 Windows 系统托盘控制
- 宠物隐藏时暂停渲染和网络刷新
- 仅清理应用自身缓存，不触碰 Codex 历史和身份验证数据

## 平台支持

| 功能 | macOS | Windows |
|---|:---:|:---:|
| 宠物可见状态联动 | ✅ | ✅ |
| 双药水与悬停倒计时 | ✅ | ✅ |
| 详情、提醒与设置 | ✅ | ✅ |
| 真机验证 | ✅ | 🧪 预览版 |

> Windows x64/ARM64 构建已通过验证，但首次签名发布前仍需在真实 Windows 10/11 设备上验证 UI、DPI、托盘与 SmartScreen。

## 安装

### macOS

```bash
git clone https://github.com/himomohi/codex-pet-hud.git
cd codex-pet-hud
./install.sh
```

卸载：`./uninstall.sh` · 同时删除日志：`./uninstall.sh --logs`

### Windows

```powershell
git clone https://github.com/himomohi/codex-pet-hud.git
cd codex-pet-hud
.\install.ps1
```

卸载：`.\uninstall.ps1` · 同时删除设置：`.\uninstall.ps1 -Data`

## 隐私保护

- 绝不读取或记录键盘输入。
- Codex 令牌仅在调用未公开文档的 ChatGPT 内部用量接口时保留于内存，不会写入设置或日志。
- 仅清理本应用拥有的缓存与临时文件。
- Codex 宠物隐藏时停止网络刷新。

> 本项目是独立社区项目，并非 OpenAI 官方产品。实时用量功能依赖未公开文档的内部接口，可能随时发生变化。

架构与安全边界请参阅[架构文档](../architecture.md)。贡献指南见 [CONTRIBUTING.md](../../CONTRIBUTING.md)，项目采用 [MIT 许可证](../../LICENSE)。
