<div align="center">

# 杀戮尖塔2 IP直连联机 (Direct IP Multiplayer)

[**English**](README.md) | [**更新日志**](Changelog.md)

![Version](https://img.shields.io/badge/Version-1.2.0-blue.svg)
![Game](https://img.shields.io/badge/Slay_The_Spire_2-Mod-red.svg)
![License](https://img.shields.io/badge/License-CC%20BY--NC%204.0-lightgrey.svg)
![Platform](https://img.shields.io/badge/Platform-Windows-blue.svg)

*一款《杀戮尖塔2》的直连联机模组。告别复杂的平台网络环境，通过 IP 地址直接与好友建立连接，并肩爬塔！*

</div>

---

本模组为《杀戮尖塔2》添加了底层的 IP 直连功能。无论你们是在同一个局域网下，还是使用内网穿透，都可以通过输入 IP 地址快速联机。

<br>

<div align="center">
  <img src="img/host-menu.png" alt="创建房间界面" width="1920"/>
  <br><br>
  <img src="img/join-menu.png" alt="加入房间界面" width="1920"/>
  <br><br>
  <img src="img/profile-menu.png" alt="玩家配置界面" width="1920"/>
</div>

<br>

---

## 🎀 核心功能

*  **IP 直连支持：** 您可以直接通过 IPv4/IPv6 地址（及端口）建立点对点/客户端-服务端连接。
*  **低延迟同步：** 专为网络环境不佳、常规匹配困难的玩家打造，提供更稳定的底层连接方式。
*  **支持离线玩家托管：** 遭遇网络波动掉线也不必重开！系统将自动托管离线玩家，直到玩家网络恢复并重新连接。
*  **虚拟局域网友好：** 完美兼容各类内网穿透与虚拟局域网工具。

---

## 🎮 玩家安装说明

### Windows

1. 从 **Releases** 页面下载最新的 `sts2-DirectConnectIP-[version].zip` 压缩包。
2. 解压并将内部的 `DirectConnectIP` 文件夹整体复制到游戏的 `<Slay the Spire 2>/mods/` 目录下。
3. 启动游戏，模组将自动启用。

---

## ⚙️ 联机与配置指南

**作为房主（Host）：**
1. 在游戏主界面点击“创建房间”，界面会弹出创建房间的联机模式。
2. 如果你的好友在外网，请将你的**公网 IP**（或虚拟局域网 IP）告知好友。
3. 本地默认监听端口为 **UDP 33771**。
4. 玩家名称及玩家 ID 默认继承自 Steam 信息，您也可以在界面内自定义更改。

**作为客机（Client）：**
1. 在游戏主界面点击“加入服务器”（或加入房间）。
2. 在弹出的输入框中填写房主提供的 IP 地址（格式如 `abc.example.com:9567` 或 `192.168.1.100:33771`）。
3. 点击连接即可进入大厅。如果连接成功，该 IP 将被自动记录，方便下次一键联机。
4. 玩家名称及玩家 ID 默认继承自 Steam 信息，您也可以在界面内自定义更改。

> **提示：** 模组配置会自动保存到 Godot 用户目录下的 `user://mods/DirectConnectIP/config.ini`。

---

## 📝 配置选项文件示例

```ini
# 档案配置
[Profile]

# 你的玩家名称
LocalPlayerName="你的玩家名称"
# 你的玩家ID (网络ID，作为唯一标识符)
LocalPlayerId=123456

# 功能设置
[Features]

# 是否启用离线玩家托管模式 (默认 true)
EnableOfflineTakeover=true
# 是否针对非官方移植版做出兼容修复 (默认 true)
EnableAndroidCompatFix=true

# 成功连接时存储的IP地址 (仅作历史记录展示)
[ServerHistory]

Server0="localhost:33771"
Server1="mod.example.com:9567"
Server2="sts2.example.com:23333"
```

---

## ⌨️ 自定义命令

**模组注册了两种命令，可在控制台中输入使用（非必须，普通玩家不推荐）：**
1. `sethostmode (steam | enet | ip)`：更改主机建房模式（此命令已弃用）。
2. `connect <ip> [port]`：通过 IP 地址直接连接至指定房间。

---

## 📄 开源协议与版权声明 (License)

本项目采用 **Creative Commons Attribution-NonCommercial 4.0 International (CC BY-NC 4.0)** 协议进行许可。

* **免费使用与修改**：你可以自由使用、学习、分发本项目的代码。**非常欢迎其他作者基于本代码进行后续的维护与二次开发。**
* **署名要求**：在分发、修改或基于本项目进行二次开发时，必须保留原作者的署名。
* **商业授权限制**：**严禁未经授权将本项目及其衍生版本用于任何形式的商业用途**（包括但不限于：收费牟利、整合包付费售卖、带有强制赞助墙的服务器等）。

**版权与商业授权申请：**
本项目所有核心代码的版权归原作者所有。未经授权的商业行为将被追究法律责任。