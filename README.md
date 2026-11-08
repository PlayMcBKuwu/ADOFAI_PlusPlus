# ADOFAI++

[![ADOFAI Version](https://img.shields.io/badge/ADOFAI-r80%2B-blue.svg)](https://store.steampowered.com/app/977950/A_Dance_of_Fire_and_Ice/)
[![Platform](https://img.shields.io/badge/Platform-Windows-lightgrey.svg)]()
[![Mod Loader](https://img.shields.io/badge/Loader-Unity%20Mod%20Manager-orange.svg)](https://www.nexusmods.com/site/mods/21)
[![Language](https://img.shields.io/badge/Language-C%23%205.0%20%2F%20.NET%20Framework-brightgreen.svg)]()

专为节奏游戏《**冰与火之舞**》（*A Dance of Fire and Ice*, 简称 ADOFAI）打造的 Unity Mod Manager (UMM) 综合增强模组（完整版）。

---

## 📖 核心功能概览

### 1. 始终允许关卡编辑器 (官方关卡一键在关卡编辑器中打开)
- **告别谱面乱码与缺失**：在任意官方关卡（如 1-X ~ 12-X、XI-X It Go、XT-X Options、XR、XS、AR 等）暂停时，暂停菜单中始终显示「在关卡编辑器中打开」选项。
- **无缝干净导出**：点击后，模组自动将当前官方关卡提取并转换为规范的 `.adofai` 自定义谱面格式，同时将高品质音频无损导出为 `song.wav`。
- **开箱即玩即测**：关卡编辑器直接加载导出后的官方关卡，音画完全同步、轨道角度 100% 精确匹配、支持直接试玩、慢速练习与魔改编辑。

### 2. 更好的日语本地化与字体修复
- **地道日语判定文本**：针对原版日语直接复用英文判定词的问题，提供自然贴切的日语本地化（如「少し早い！ / 少し遅い！」与韩语、中文风格高度统一；Ready / Go 自然本地化）。
- **中文字体平滑替换**：原版日文字体在部分分辨率下纤细且显示欠佳，模组内置「日语界面下使用中文字体」开关（默认开启），让日文排版清晰美观。

### 3. 三大判定模式随时切换
通过 UMM 控制台（游戏内按 <kbd>Ctrl</kbd> + <kbd>F10</kbd>）随时切换：
- **原生 PC 模式** (*Vanilla PC*)：严格 40ms / 标准 65ms / 宽松 90ms，支持指示器增强与轨道染色。
- **移动端判定模式** (*Mobile*)：90ms / 70ms / 50ms，且倍速试炼绝不缩减，在 PC 上还原手机触屏手感。
- **自定义判定模式** (*Custom*)：支持时间 (ms) 与角度 (°) 双轨驱动，支持滑块突破输入、百分比乘算器与 3 组快速档案槽位。

### 4. 判定指示器与轨道染色
- **判定指示器**：在 PC 严格/标判下打出及格 (Counted) 时实时重测，提示「标判！」或「宽判！」。
- **另类指示器+**：提供正负符号前缀（`-宽判！` 太快 / `+标判！` 太慢）或方向前缀（`L-太快！` / `N-太慢！`）。
- **失误判定指示器**：即使在「太快！！」与「太慢！！」时也能显示是否落在宽判/标判窗口。
- **边框与轨道染色**：可独立切换浮动文字彩色边框，以及打出标判/宽判时将轨道实时染为灰色或绿色。

### 5. 辅助与实战练习工具 (受安全总开关保护)
- **安全免责声明**：严禁在多人联机游戏中使用！模组内置免责声明确认机制，确保玩家知情与安全。
- **不死模式 (No Fail)** 与 **自动播放 (Autoplay)**。
- **自动播放+ (Autoplay+)**：双通道拟人按键算法，精准驱动 KeyViewer，支持 16 个键位可视化点选绑定与 Jipper 资源包一键同步。
