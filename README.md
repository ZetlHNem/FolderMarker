# 文件夹标记 · FolderMarker

给 Windows 资源管理器加一个「文件夹标记」右键菜单，用 7 种颜色给文件夹上色。
图标直接取自**系统原生文件夹图标**（Windows 11 的 imageres 资源），只把色相换掉，
因此形状、渐变、阴影和系统完全一致，只是整只文件夹变成标记色 ——
16×16 的小图标下也能一眼认出颜色。

## 安装

双击 `dist\文件夹标记-安装包-v1.8.0.exe`，标准中文向导：欢迎 → 选目录 → 安装 → 完成。
**安装时会弹一次 UAC**（需要它把签名证书写进本机受信任根、并注册一个应用包），点「是」即可。
装完可以在「设置 → 应用 → 已安装的应用 → 文件夹标记」里卸载。

安装位置：`%LOCALAPPDATA%\FolderMarker`

## 使用

在资源管理器里右键**选中一个文件夹**，**第一层菜单**里就有「文件夹标记」，点开挑颜色。
（文件夹里的空白处不再出现这个菜单 —— 那里没有选中对象，给了也没用。）

- 颜色：红 / 橙 / 黄 / 绿 / 蓝 / 紫 / 灰
- 「清除标记」恢复系统默认图标

点完**不到半秒**就变，已打开的窗口也会跟着重绘，不弹任何窗口、不闪命令行。
进程整体（含启动）50–90 ms，其中真正干活的只有 20 ms 左右。

**文件夹自身的「修改日期」最多被挪动 2~4 秒**（资源管理器只显示到分钟，看不出来）——
这是为了让缩略图缓存失效、图标立刻换色，详见下面「不留下任何痕迹」。

## v1.8.0 加了什么 / 优化了什么

**新功能**

- **自定义颜色**：右键菜单「文件夹标记 → 自定义颜色...」弹系统取色器，
  或命令行 `FolderMarker.exe set c3a7bd0 <文件夹>`（c + 6 位十六进制 RGB）。
  和预设 7 色走完全相同的渲染管线（原版三明治 + 纯壳参照换色）。
- **恢复上次颜色**：右键「恢复上次颜色」或 `undo` 命令，改色前自动记录。
- **托盘**：`FolderMarker.exe tray` 常驻系统托盘 —— 已标记文件夹快达、
  立即巡检、暂停/恢复巡检、退出守护。安装后开机自启。
- **diagnose / selftest 命令**：`diagnose <文件夹>` 一条命令输出 ini / 缓存 ico
  逐帧 alpha / 原生图缓存 / 纯壳索引 / 实时判定全部状态（同时落盘 diagnose.txt）；
  `selftest` 在临时目录跑 29 项完整回归（标色/换色/清除/空文件夹/崩溃恢复/
  watcher/自定义色/undo），升级后一键自检。

**稳定性**

- **.fm_tmp 崩溃恢复**：抓原版缩略图时 desktop.ini 会临时改名，进程若在这
  中间被杀会丢标记。启动/巡检时自动把残留移回原位。
- **原版缩略图缓存**：cache\native\<hash>.png 绑定内容签名缓存 Windows 原版
  三明治，换色不再需要「摘 desktop.ini → 等 shell 重试」整套流程 ——
  实测换色从 ~600ms 降到 114ms。缓存读入时过可靠性校验，坏图自动丢弃重抓。
- **imageres 索引自适应**：不同 Windows 版本的文件夹图标位置可能不同，
  进程内首次用壳时拿临时文件夹实测验证 5/6 号索引，不对就自动搜索并缓存；
  缓存索引过期时合成失败会自愈重试。
- **首次标记跳过摘标记**：文件夹本来没有 IconResource 时不再白做一次 rename。

**性能**

- **FileSystemWatcher 即时响应**：本地盘已标记文件夹挂 watcher，内容一变
  0.5~1 秒内重建预览（轮询兜底网络盘）。事件过滤自身写动静防止自触发，
  去抖 500ms，同文件夹不并发重建。

## v1.7.1 修了什么

- **染色文件夹不再有「脏边」**：旧版按「是壳 / 是内容」二值分类，三类像素会漏网成杂色——
  ① 标签、边缘处原图与纯壳有细微差异（d≈27）的像素被误当内容，保留成黄色杂点；
  ② 内容卡片与底板交界处的抗锯齿混合像素整圈原样保留，形成黄/黑描边；
  ③ 纸张折角等细窄底板结构周围的浅黄渍痕。现在硬分类之后做**形态学闭合**（补漏网洞）
  + **两遍模糊**（交界在内容侧形成渐变），最终 mask 取 max(闭合, 模糊)，
  输出按 `原图 + (染色 - 原图) × mask` 比例合成 —— 混合像素不再非黑即白，
  深处内容仍一个字节不动。实测：内容窗口内黄棕脏像素 5 → 0，
  标签区漏网近壳像素 19 → 0，深处内容与原版逐字节一致（仅 ico 渲染管线 ±2 舍入）。

## v1.7.0 修了什么

这一版没动界面，全是功能上的修正（都是实测复现过的）：

- **兜底写入不再清空 desktop.ini**：shell 接口写不进去时（ACL 拒绝 / ini 只读），
  旧逻辑会把整个 `[.ShellClassInfo]` 段重写，把用户的 `InfoTip`、`IconArea_Image`
  一起删掉。现在只替换 `IconResource` 三行。
- **图片删光后不再挂着旧照片**：文件夹里的图/视频没了，图标会降级成纯色，
  缓存一起清掉；以前会永远停在那张已经不存在的预览图上，每轮巡检还白试一次。
- **后来放进图片也会自动出预览**：纯色标记的文件夹出现图片/视频后，会升级成内容预览。
  只在文件夹 mtime 变了时才去枚举文件，平时一轮巡检只有一次 stat 的开销。
- **跨电脑同步不再漏纯色标记**：扫描共享盘时同时识别 `cache\<hash>-<color>.ico` 和
  `icons\<color>.ico`。以前只认前者，不含图片的文件夹在另一台永远同步不过去。
- **另一台清掉标记后，这台的清单会自动收掉**：巡检发现 ini 里已经没有本程序的图标，
  就把条目和缓存一起移除，不再留「(未标记)」幽灵行。
- **「停止守护」不再误杀**：只按 `watch.pid` 里的 PID 杀守护进程，
  不会把正在标色 / 批量处理的无窗口子进程一起打死。
- **离线共享盘不再拖住巡检**：每轮按共享盘探测一次可达性（1.2 秒超时），
  不可达整根跳过；「清理失效条目」也不会再把离线盘上的有效记录删掉。
- **自适应扫描间隔真的生效了**：大根扫一次超过 2 秒后拉长到 30 分钟，
  以前每轮循环都会被设置值打回 5 分钟。
- **标记后图标会立刻换过来**：资源管理器的「文件夹缩略图缓存」按「路径 + 修改时间（2 秒粒度）」
  做键，时间不动就会一直显示标记前那张黄壳缩略图，看起来像「cache 里有图标但没换上」。
  现在写完图标会先通知 shell、再把修改时间挪 2~4 秒，缓存立即失效（实测 2~3 秒内换图）。
- **非图片/视频文件夹也能出彩色三明治**：以前 .ico / .svg / .pdf 等「能出缩略图但不在
  白名单」的文件夹，合成逻辑直接跳过、只给纯色图标，结果被 Windows 自己的「黄壳+内容」
  缩略图盖掉。现在先按常见图片/视频扩展名快速匹配，没命中就对任意文件试抓 shell 缩略图，
  只要能出预览就做彩色三明治。
- **升级 0 次重启资源管理器**：shell 扩展 DLL 是个稳定的菜单 shim（只负责启动主程序），
  跨版本不变。升级时检测到「包已注册 + DLL 未变」就完全跳过 MSIX 注册 —— 不碰 Add-AppxPackage，
  资源管理器一次都不用重启。只有首次安装（1 次）或 DLL 真改了（罕见，需手动 bump ShellExtVer）
  才需要注册。
- **安装不再反复重启资源管理器**：以前每次安装都固定「卸载菜单扩展包 + 重新注册」，
  而 Windows 卸载/注册带 shell 扩展的 MSIX 时会重启资源管理器，看起来像连着重启两三次。
  现在包版本随程序版本递增，同版本重装直接跳过注册（0 次），升级走原地更新（1 次），
  只有原地更新失败才退回卸载+重装。
- **缓存会回收**：文件夹被删/移走、降级成纯色之后，cache 里的 ico 和 `.sig` 不再永久堆积。
- 顺带：双击 exe 直接开管理台（以前静默退出）；`set` 的 2/3/4 号错误现在有提示；
  多选批量失败不再弹一串模态框；`marked.txt` 读改写加了锁；COM 缩略图对象显式释放；
  守护页的「每 60 秒 / 每 5 分钟」文案改成跟随设置显示。

## 实现原理

调 Shell 自己的接口 `SHGetSetFolderCustomSettings(FCSM_ICONFILE, FCS_FORCEWRITE)`：

- 它负责把图标写进目标文件夹的 `desktop.ini`（`[.ShellClassInfo] IconResource=...`）
- 同时**强制刷新该文件夹的外观缓存**，这是唯一能让「已打开的窗口」立刻重绘的入口

改完再顺手清理：清除标记时把 shell 留下的空 `IconResource` 和 `ReadOnly` 属性一起收掉，
文件夹彻底恢复原样，不留垃圾文件。

### 自带图标的文件夹：只染外壳

有的文件夹本来就被换过图标，最常见的是「文件夹外壳 + 内部小图标」的合成图
（比如「常用软件」这种放了一排快捷方式的文件夹）。这类文件夹如果按普通办法整颗换成
纯色文件夹，用户自己那层图案就没了。

所以标记前会先看 `desktop.ini` 里有没有**用户自己的** `IconResource`：

1. 有 → 用 `PrivateExtractIcons` 把原图标按 10 个尺寸（16…256）取出来
2. 逐像素判断「这是不是黄色外壳」：色相 20°–82°、饱和度 ≥ 10%、明度 ≥ 6%
3. 只把这些像素的**色相换成目标色、饱和度等比缩放**，明度与 alpha 原样保留
   —— 于是渐变、阴影、内部小图标全在，外壳变成标记色
4. 合成结果按 `<路径哈希>-<颜色>.ico` 写进 `%LOCALAPPDATA%\FolderMarker\custom\`，
   `desktop.ini` 指向它；清除标记时这个文件会被删掉，图标还原成用户原来那张

外壳像素不到 6%（说明原图标根本不是文件夹形状，比如是个 App 图标）时不做染色，
退回成内置的整颗彩色文件夹 —— 宁可换掉，也比不给反馈好。
色相带判断带来的副作用是：如果原图标里正好有橙色/黄色的小图案，它也会跟着变颜色。

### 带内容预览的文件夹：只换外壳颜色（v1.7.0 起，v1.7.1 修杂色边）

文件夹里有 Windows 能出预览的内容时，标记不再把它换成一颗纯色文件夹，而是：

1. 临时把 `desktop.ini` 移走（二进制重命名，保留 UTF-16 编码与隐藏/系统属性），
   向 shell 要这张文件夹的**原版三明治缩略图**，随后立刻原样移回
2. 从 `imageres.dll` 取 5 号背板 + 6 号前盖，叠成一张**纯黄色壳**。
   实测它与原版三明治里的壳像素逐像素完全相同
3. 纯壳图当参照逐像素分类「壳 / 内容」，再经形态学闭合 + 模糊得到软 mask
   （v1.7.1：闭合补上标签、边缘处与纯壳有细微差异的漏网像素，模糊让
   壳/内容交界的抗锯齿自然过渡，不留二值边界的杂色）
4. out = 原图 + (染色 - 原图) × mask：混合像素按比例合成，深处内容
   一个字节不动；alpha 直接取纯壳形状，透明区天然干净

内容、卡片、padding、大小、位置全部来自 Windows 原图，程序里没有任何几何参数，
也不按颜色猜背景。抓不到原版三明治（比如空文件夹）时退回内置纯色 ico。

**ico 只放本机缓存，绝不写进被标记的文件夹 / 共享盘**：

```
[.ShellClassInfo]
IconResource=%LOCALAPPDATA%\FolderMarker\cache\61f71f23-red.ico,0
```

- 缓存目录 `%LOCALAPPDATA%\FolderMarker\cache\`，每个标记一个 ico + 一个 `.sig` 内容签名；
- `desktop.ini` 用环境变量路径，两台电脑各自展开到自己的缓存；
- 清除标记时自动删掉对应的缓存 ico；
- 普通文件夹（没有图片/视频）仍直接用安装包里现成的纯色 ico，不产生任何缓存文件。

### 重启不缺图 & 内容变了预览跟着变

缓存放在持久目录，关机不会丢；登录自启的守护进程（`FolderMarker.exe watch`）
开机先**强制全量重画一次**（相当于「重启后重新获取」），之后默认每 60 秒巡检：

- 缓存 ico 被删 / 丢失 → 立刻重建，不会掉色成默认黄文件夹；
- 文件夹里增删文件（目录 mtime 变）或封面内容被改（文件 mtime/大小变）→
  签名变化 → 重新抓缩略图、重新合成；
- 实测**同名覆盖 ico 内容后资源管理器自动换图**，不用改 desktop.ini、不用通知 shell；
- 合成失败时保留旧 ico，绝不把图标搞没。

可选命令：

| 命令 | 作用 |
|---|---|
| `FolderMarker.exe refresh none` | 巡检一次（只处理缺失/变化的） |
| `FolderMarker.exe refresh none force` | 强制全部重画 |
| `FolderMarker.exe import <共享盘根>` | 另一台电脑导入别人标好的文件夹并生成自己的缓存 |

### 跨电脑 / 网络共享盘同步

共享盘上同步的只有 `desktop.ini`（一行环境变量路径 + 状态记录）；
ico 是**每台电脑本机的缓存**，不跟着盘走，所以每台电脑各自生成一份——但生成的
**文件名和颜色都写在 desktop.ini 里**，另一台照抄即可，不需要路径写法一致。

**操作步骤（另一台电脑只需做一次）**

1. 两台都装好（默认目录 `%LOCALAPPDATA%\FolderMarker`）；
2. 在 A 电脑上正常右键标颜色；
3. 在 B 电脑上跑一次：

```
FolderMarker.exe import "\\Win-5mrur8btloj\漫剧项目\10月\诱吻桑藜\视频素材"
```

⚠️ **给最深的那个根**，别给整个 `\\Win-...\漫剧项目`。实测同一个共享盘：

| import 的根 | 目录数 | 主程序扫一次 |
|---|---|---|
| 整个 `漫剧项目` | 8407 | **10.8 秒** → 自动改成 30 分钟一次 |
| 精确到 `视频素材` | 195 | **1.2 秒** → 保持 5 分钟一次 |

根越大，每次扫盘的网络往返越多。程序会自动把慢扫描（>2 秒）的间隔从 5 分钟
拉长到 30 分钟，但给精确根还是最省事的。

程序递归扫这个根下所有 `desktop.ini`，把带标记的文件夹写进本机 `marked.txt`、
立刻生成自己的缓存 ico，并把这个根记进 `roots.txt`。

**之后全自动**（v1.5.15 起）：守护进程每轮巡检会按 `roots.txt` 定期扫共享盘根
（小根 5 分钟、大根自动 30 分钟，开机立即扫一次），所以 A 上的这些变化 B 都会自己跟上：

| A 上发生 | B 上怎么同步 |
|---|---|
| 新标一个文件夹 | 扫到新 `desktop.ini` → 加入 `marked.txt` → 生成缓存 ico |
| 换颜色 | ini 里的图标名变了 → 本机缺这个 ico → 按新颜色重建，旧色缓存清掉 |
| 文件夹里换了图/增删文件 | 两边各自算内容签名 → 各自重画，预览都跟着变 |
| 清除标记 | 图标行没了 → 巡检跳过，显示回默认文件夹 |

换用户名、换盘符、UNC 与映射盘混用都不受影响（`%LOCALAPPDATA%` 各自展开、图标名照抄）。
自定义安装目录会让环境变量路径指不到，务必用默认目录。

### 管理台（v1.5.18 起，v1.5.19 起按 WinUI 3 规格重做）

右键任意文件夹 → **文件夹标记 → 管理…**（齿轮图标，菜单最后一项），打开一个
Windows 11 设置页风格的管理窗口：

| 页面 | 能做什么 |
|---|---|
| **标记清单** | 所有已标记文件夹、当前颜色、缓存图标是否正常；搜索（`Ctrl+F`）、按颜色筛选、选中多行批量改色 / 批量清除；选中一行在下方显示**图标大图预览**（可切 64/128/256 看清晰度）；**双击一行直接在资源管理器打开**；把文件夹拖进窗口直接标色；一键清理失效条目 |
| **守护进程** | 状态灯（运行中 / 未运行）、上次活动时间、启动 / 停止、立即巡检一次、强制全部重画 |
| **共享盘同步** | 图形化添加 / 删除共享盘根、一键导入并生成缓存，不用再敲命令 |
| **日志** | 直接看 `refresh.log` 尾部，可跳转到文件 |
| **设置** | 开机自启开关、巡检间隔、共享盘扫描间隔、内容变化自动跟随开关；底部「清除全部标记」危险操作（带数量确认） |
| **关于**（固定在左下角） | 版本号、程序目录、数据目录、已标记数量、缓存文件数与体积、图标资源档数、打开数据目录、重建图标缓存、查看 `error.log` |

`F5` 刷新全部页面；窗口大小会记住（存在 `settings.ini`）。

界面用 WPF 实现，但**外观逐项按 WinUI 3 规格复刻**（`src\ui\WinUi.cs` 集中放色板与控件模板）：

- **自绘标题栏**：48px 高，左侧应用图标+名称，右侧 46×32 的最小化/最大化/关闭，
  悬停关闭变红（#C42B1C）；`WindowChrome` 负责拖拽与四边缩放
- **Win11 窗口形态**：`DWMWA_WINDOW_CORNER_PREFERENCE` 圆角、`DWMWA_BORDER_COLOR=NONE`
  去掉系统那圈 1px 灰边；自己接管 `WM_GETMINMAXINFO`，最大化严格贴合工作区（不溢出 7px）
- **NavigationView**：296px 左栏、40px 项高、圆角 4、选中态为 4% 黑底 + 左侧 3×16 强调色 pill
- **控件模板全部重写**：Button / AccentButton / SubtleButton / ComboBox（含圆角 8 带阴影的
  下拉弹层）/ ListView / ListBox / TextBox（底边框，聚焦转强调色 2px）/ ScrollBar
  （2px 细条，悬停展开到 6px，无箭头）/ ToolTip（深色浮出）
- **ContentDialog 风格对话框**：烟雾遮罩 + 圆角卡片，替换掉系统 MessageBox（那是 Win32 老式对话框）
- **Win11 字号阶梯**：标题 28 SemiBold、正文 14、说明 12；字体 Segoe UI Variable → 微软雅黑回退

为什么不用真 WinUI 3：那需要 .NET 8 SDK + Windows App SDK + WindowsAppRuntime 运行时 +
MSIX 打包，安装包会从 2.7MB 涨到几十 MB 且要求用户先装运行时；而 WinUI 3 的视觉规格
（色板、控件状态、圆角、字号）在 WPF 里可以逐项实现，零额外依赖、装完即用。

### 出现在 Windows 11 的主菜单里

Windows 11 的右键主菜单只加载**带包身份的 `IExplorerCommand`**。传统写法（`...\shell\FolderMarker\command`
或 `SubCommands`）一律会被收进「显示更多选项」——实测过：哪怕把原生 COM 的 `InprocServer32`
和 `ExplorerCommandHandler` 都指对了，主菜单里依然什么都不出现。

所以这里用了微软要求的那套：

1. `src\shell\shell_native.c` —— 纯 C 手写 vtable 的 COM 服务器，实现 `IExplorerCommand`（父命令）
   和 `IEnumExplorerCommand`（7 色 + 清除共 8 个子项），用 zig cc 编译成
   `FolderMarker.Shell.dll`
2. `packaging\AppxManifest.xml` —— sparse 包清单，用 `com:SurrogateServer` 声明这个 COM 类，
   再用 `desktop4:FileExplorerContextMenus` 把它挂到 `Directory`（只有文件夹本身）
3. `src\shell\shell.manifest` —— 包身份清单，必须用 `mt.exe` 嵌进 DLL，
   否则资源管理器会静默不加载（表现为菜单项根本不出现）
4. `build_msix.ps1` —— `makeappx` 打包 + 自签名证书签名

包必须由**受信任证书**签名，否则 `Add-AppxPackage` 会直接报 `0x800B0109`
（「已处理证书链，但是在不受信任提供程序信任的根证书中终止」）。注意 AppX 部署服务跑在
SYSTEM 上下文，**只认本机根存储**，装到当前用户根里没用——所以安装器必须提权。

安装器做完包注册后，会把旧的注册表菜单键删掉；不删的话「显示更多选项」里会多出一份重复的
「文件夹标记」。卸载时反过来：`Remove-AppxPackage` → 摘证书 → 删文件。

### 不留下任何痕迹

写 `desktop.ini` 会让 NTFS 把文件夹的**修改时间、访问时间刷成当前时间**，
shell 还会顺手给文件夹打上**「只读」**属性 —— 用户一打开属性面板就发现文件夹被动过。

所以每次操作都会：

1. 先记下文件夹原来的修改时间、访问时间和属性（同时写进 `desktop.ini` 的 `[FolderMarkerState]` 段，
   否则清除时"标记前的状态"早被标记过程污染了）；
2. 调 shell 接口写图标；
3. 把 shell 加的 `ReadOnly` 换回 `System` —— 两者都能让资源管理器认 `desktop.ini`，
   但 `System` 在属性对话框里看不出来，不会让用户以为文件夹变成只读了；
4. 用 UTC 时间把原来的修改时间、访问时间写回去（UTC 避免夏令时换算把时间改掉几分钟）；
   然后**再往后挪 2~4 秒** —— 资源管理器的「文件夹缩略图缓存」是以
   「路径 + 修改时间（2 秒粒度）」为键的，时间完全不动的话，标记前那张
   「黄壳 + 内容」的缩略图会一直命中，用户就会看到「cache 里有新图标，窗口里还是旧样子」。
   同一文件夹反复换色时基准固定为最初的修改时间，偏移不会累积。

清除时按 `[FolderMarkerState]` 里的记录**整份还原**：原本是只读的还它只读，原本是系统
文件夹的还它系统，原本有自定义图标的也把 `IconResource` 还回去。整段 `[.ShellClassInfo]`
不会被删掉 —— 用户自己放在里面的 `InfoTip`、`IconArea_Image`（文件夹背景图）都保留。

结果是：标记、换色、清除全过程中，文件夹的「修改日期」始终是原来那一天（最多偏几秒），
创建时间不变，属性面板里也没有任何勾选变化。清除时按最初记录精确还原时间。

### 超长路径（超过 260 字符）

夹在深层目录里的文件夹（路径 260+ 字符）以前会直接报「不存在」。原因不在 shell，
而在 .NET Framework：程序没有 `app.config`，运行时把「目标框架」按 4.0 处理，
于是走 legacy 路径逻辑 —— `Directory.Exists` 对超长路径**默默返回 false**，
`DirectoryInfo` 直接抛 `PathTooLongException`。反而 shell 接口
（`SHGetSetFolderCustomSettings`）对长路径是支持的，加不加 `\\?\` 都返回 S_OK。

修法两件一起做才行：

1. 在源码里显式声明 `[assembly: TargetFramework(".NETFramework,Version=v4.6.2")]`，
   让运行时启用长路径行为（4.6.2 是默认开启长路径的版本）；
2. 所有文件系统调用走 `Io()`，路径 ≥240 字符时补 `\\?\` 前缀（UNC 路径用 `\\?\UNC\`）。

系统级的 `LongPathsEnabled` 开关不用动 —— 加了前缀就能用，跟那个注册表项无关。

### 卸载时还原

标记过的文件夹路径记在 `%LOCALAPPDATA%\FolderMarker\marked.txt`，一行一个；
清除标记时从清单里删掉。卸载时安装程序在**删文件之前**调用
`FolderMarker.exe cleanup none`，逐个还原（属性、时间戳、用户原有图标），然后删掉清单。

两个安全阀：清单里已经不存在的路径直接跳过；`desktop.ini` 里的 `IconResource`
如果不再指向本程序的图标（用户后来自己换过），也跳过不动 —— 不能把人家后设的东西删了。

### 为什么不能自己写 desktop.ini

这是本项目最大的坑，实测结论（Windows 11，全部截图逐帧核对过）：

| 做法 | 结果 |
|---|---|
| 只写 `desktop.ini` | 不变色 |
| 写 + `SHChangeNotify(SHCNE_UPDATEITEM/UPDATEDIR)` | 不变色 |
| 写 + `SHChangeNotify(SHCNE_ASSOCCHANGED)` | 不变色 |
| 写 + 给 `SHELLDLL_DefView` 发 `WM_COMMAND 0x7103`（等于 F5） | 不变色 |
| 写 + COM `Shell.Application` 的 `Window.Refresh()` | 不变色 |
| 写 + 改文件夹时间戳 / 删了重建 ini / 切换属性 | 不变色 |
| **`SHGetSetFolderCustomSettings`** | **很快变色** |

资源管理器是按「文件夹」缓存外观的，而且**连 F5 都不会让它重读 `desktop.ini`** ——
所以才会出现「标记完要等 40 秒~1 分钟才变」的现象。通知类 API 拿它没办法，
只有走 shell 自己的设置接口才会真正刷新。

> 顺带一个反直觉的点：如果图标的**文件名每次都换新的**（曾经这么干过），
> 资源管理器会继续引用旧路径，而旧文件已被删掉，结果是从彩色变成**默认黄色**，
> 比不生效更难看。所以现在固定用 `icons\<颜色>.ico`，不再复制副本。

核心程序是 C# 编译的 **GUI 子系统原生 EXE**（91 KB，含图标资源），从系统层面就不分配控制台，
所以不会闪黑框，也不依赖 Python / PowerShell 运行时。

写不进去的时候（只读卷、没有权限的网络位置）不再闷头失败，会弹一个提示框说明原因 ——
之前的版本是静默返回，用户只会觉得"点了没反应"。

### 应用图标

项目里有**两个**图标，用途不同：

| 文件 | 用在哪 | 样子 |
|---|---|---|
| `icons/app.ico` | 安装包、EXE 自身、卸载项（`/win32icon` 编进资源） | 深墨色圆角底板 + 彩虹渐变文件夹 |
| `icons/menu.ico` | 右键「文件夹标记」分组 | 与 app.ico 同一个 logo，尺寸只到 48 px |

`menu.ico` 就是 app 图标的缩小版（`gen_menu_icon.py` 直接复用 `gen_app_icon` 的合成逻辑，
只是尺寸集换成 16/20/24/32/40/48、超采样提到 8×8），**没有**另外设计一套视觉。

早期版本试过「透明底蓝文件夹 + 红黄绿三个小点」，最后放弃了：16 px 下三个点糊成一片彩色噪点，
而且和 app.ico 完全是两套观感，右键菜单里像另一个软件的图标。菜单图标现在和 app 图标能对上号。

`icons/app.ico` 由 `gen_app_icon.py` 生成，两部分各取所长：

- **文件夹**用的是「换色」而不是重绘：取系统原生文件夹图标（`icons/yellow.ico`，
  shell 渲染出来的位图），用它的 alpha 当形状蒙版 —— 标签页、圆角、边缘和资源管理器里一模一样；
  再把原图明度归一化成 shading（顶面高光、标签页暗一档、底边压暗全保留），
  乘上对角彩色渐变得到新颜色。
- **底板**是矢量绘制的圆角矩形，竖向渐变 + 左上柔光，4×4 超采样抗锯齿。
- 两者在超采样网格上合成后降采样，整个图标**没有投影 / 阴影**。

配色可切：`python tools/gen/gen_app_icon.py rainbow|candy4|sunset|aurora indigo|ink|slate build\x.ico`，
第一个是文件夹渐变，第二个是底板。默认 `rainbow ink`（深墨色底板）。
`ink`（近黑底板）对比最强烈，`slate`（浅灰底板）在白底界面里边界会偏弱。
改完记得重新编译打包。

## 项目结构

```
FolderMarker/
├─ dist/文件夹标记-安装包-v2.3.7.exe   ← 成品安装包（版本号随发版更新）
├─ dist/FolderMarkerShell.msix         ← 右键菜单扩展的 sparse 包（已签名）
├─ src/                                ← 源码（按职责分 4 个子目录）
│  ├─ core/   FolderMarker.cs + 17 个 partial  ← 程序核心（入口/图标/巡检/规则…；partial 已去前缀，如 Cache.cs / Mark.cs）
│  ├─ ui/     ManagerWindow + ColorPicker + WinUi ← WPF 管理台与界面
│  ├─ shell/  ShellExtension.cs / shell_native.c / shell.manifest ← 右键菜单 COM 扩展
│  └─ _backup/ 拆分前的 .bak 备份（不参与编译）
├─ packaging/                          ← sparse 包内容（AppxManifest.xml + logo）
├─ icons/*.ico                         ← 7 色文件夹图标 + app.ico + menu.ico
├─ FolderMarker.iss                    ← Inno Setup 安装脚本
├─ build_exe.ps1                       ← 编译 EXE（含图标资源）
├─ build_shell.ps1                     ← ⚠ 旧脚本：编译的是无用的托管 DLL（9KB），勿用来覆盖正式产物；原生扩展由 build_msix.ps1 用 zig 编译
├─ build_msix.ps1                      ← 打包 + 签名 sparse 包（需要 makeappx / signtool）
├─ build/audit.py / audit2.py          ← 边界体检脚本（可指到任意版本的 exe 跑）
└─ tools/
   ├─ extract_native.py                ← 抓系统原生文件夹图标（多尺寸）
   ├─ tint_icons.py                    ← 上色并生成 .ico
   ├─ preview.py                       ← 生成各尺寸效果对比图
   ├─ preview_app_icon.py              ← 导出 app.ico 各尺寸预览
   ├─ shell_test.cpp                   ← 诊断用：直接调 DLL，打印 GetFlags / 子项 / Invoke
   ├─ IconProbe.cs / build_probe.ps1   ← 诊断用：新进程问 Shell「这个文件夹此刻是什么图标」
   ├─ ThumbProbe.cs                    ← 诊断用：问 Shell 要「此刻真正画出来的那张图」（能看到缩略图）
   ├─ build_thumbprobe.ps1             ← 编译 ThumbProbe.exe
   └─ gen/                             ← 应用/菜单/向导图标生成脚本（改配色时用）
      ├─ gen_app_icon.py                ← 绘制应用 / 安装包图标（底板 + 彩色文件夹）
      ├─ gen_app_icon_plain.py          ← 同上，但没有底板（纯彩色文件夹）
      ├─ gen_menu_icon.py               ← 绘制右键菜单小图标
      ├─ gen_wizard.py / gen_wizard_images.py ← 绘制安装向导品牌图
      └─ gen_action_icons*.py / gen_badge_icons.py ← 右键菜单动作图标
```

程序支持的动作（都从注册表菜单调用）：

| 命令 | 作用 |
|---|---|
| `FolderMarker.exe set <颜色> "<文件夹>"` | 标记 / 换色，颜色可选 `red orange yellow green blue purple gray` |
| `FolderMarker.exe clear none "<文件夹>"` | 清除标记并还原原始属性、时间戳、用户原有图标 |
| `FolderMarker.exe cleanup none` | 批量还原「标记清单」里的所有文件夹（卸载前调用，也可手动跑） |

`tools\IconProbe.exe <文件夹>` 会打印主色名、色相、平均色和像素指纹，
再遇到「图标不对」的问题，先用它判断是缓存没刷新还是图标文件本身有问题。

体检脚本：`FM_EXE=<某个 exe> python build/audit.py`、`python build/audit2.py`，
覆盖中文/空格/`[]&;,`/百分号路径、已有的 `desktop.ini`、只读与系统属性、
7 进程并发、权限拒绝、超长路径、junction、卸载清理。

## 想改颜色 / 重新生成图标

改 `tools/tint_icons.py` 里的 `COLORS`（色相、饱和度）或 `GRAY_FACTOR`，然后：

```powershell
python tools/tint_icons.py     # 重新生成 7 个 .ico
python tools/preview.py        # 生成 build\preview_colors.png 看效果
python tools/gen/gen_app_icon.py   # 重新生成应用图标 icons\app.ico
python tools/gen/gen_menu_icon.py  # 重新生成右键菜单图标 icons\menu.ico
python tools/gen/gen_wizard.py     # 重新生成安装向导品牌图 build\wizard_large.bmp / wizard_small.bmp
.\build_exe.ps1                # 需要动代码时重新编译主程序（只需系统 .NET，无额外依赖）
.\build_msix.ps1               # 改了 shell_native.c 时：编译原生 COM 扩展 + 打包签名 MSIX
```

### clone 后如何准备构建工具链

主程序（`build_exe.ps1`）用系统自带的 .NET 编译器，clone 下来直接能编。
只有构建右键菜单扩展（`build_msix.ps1`）需要两个第三方工具，仓库里已带上原始包：

```powershell
# 1) zig 编译器：解压 zig010.whl，取其中的 ziglang\zig.exe 放到 tools\zig\zig.exe
#    （脚本也支持直接用 tools\zigpkg\ziglang\zig.exe，或 pip install ziglang）
# 2) Windows SDK 工具：解压 sdktools.zip，把 bin\10.0.22621.0\x64\ 放到
#    tools\sdktools\bin\10.0.22621.0\x64\  (mt.exe / makeappx.exe / signtool.exe)
```

> zig 0.10 在某些环境会偶发堆损坏崩溃，`build_msix.ps1` 已有降级：编译失败时复用
> 已有 DLL（`shell_native.c` 没改动时成品等价）。这两个包都在 GitHub 单文件 100MB
> 限制内，解压目录（`tools\zigpkg` / `tools\sdktools`）不入版本库。

自签名证书在 `Cert:\CurrentUser\My`（`CN=FolderMarker`），私钥 `build\FolderMarker.pfx`
**绝不入版本库**。换了证书后记得同步更新 `FolderMarker.iss` 里 `RemoveShellExtension` 的指纹。

向导品牌图（左侧 164×314 深色栏）与应用图标同源：复用 `gen_app_icon.py` 的
`folder_sprite()` 取原生文件夹形状上色，只是画布改成竖版、底板铺满、下方补标题和七色圆点。
`wizard_small.bmp` 直接把 `icons/app.ico` 合成到白底，保证两个图标的观感一致。

改了图标后重新打包，需要 Inno Setup 7 的 `ISCC.exe`（默认在
`%LOCALAPPDATA%\Programs\Inno Setup 7\`）：

```powershell
& "$env:LOCALAPPDATA\Programs\Inno Setup 7\ISCC.exe" FolderMarker.iss
```

## 已处理的坑

| 问题 | 原因 | 处理 |
|---|---|---|
| 图标不生效 | `desktop.ini` 不是 UTF-16 + 缺 BOM、文件夹缺属性 | 按规范写，显式补 BOM |
| 点一下闪黑框 | 早期版本启动 `powershell.exe` | 改成 GUI 子系统 EXE |
| 标记后要等几十秒才变色 | 资源管理器按文件夹缓存外观，通知类 API 不触发重读 | 改用 `SHGetSetFolderCustomSettings` |
| 换色后变成默认黄 | 图标路径每次都换新，旧文件又被删，缓存引用悬空 | 固定图标路径，不再复制/删除 |
| 快速连点两次文件损坏 | 两个进程同时读改写 `desktop.ini` | 命名互斥体串行化 + 乱码检测 |
| 大尺寸图标模糊 | 只做了单一尺寸 | 每次生成 16/20/24/32/40/48/64/96/128/256 全部尺寸 |
| 标记后「修改日期」变成今天 | 往文件夹里写 `desktop.ini` 会刷新目录时间戳 | 写完把修改/访问时间按原值写回 |
| 属性面板里文件夹变成「只读」 | shell 写自定义图标时会给文件夹打 `ReadOnly` | 换回 `System` 属性，功能不变且界面上不可见 |
| 安装到最后弹「无法执行 ie4uinit.exe，错误代码 2」 | `ie4uinit.exe` 只在 64 位 `System32` 里有，`SysWOW64` 下没有；32 位安装程序里的 `{sys}` 正好指向 `SysWOW64` | 改用 `{sysnative}`，并在 `[Code]` 里先检查文件存在再执行，找不到就安静跳过 |
| 卸载时同一错误连弹十几次 | Inno 覆盖安装会"追加"卸载日志，历史上每条 `[UninstallRun]` 都留在 `unins000.dat` 里 | 同上，旧 `[UninstallRun]` 已移除；彻底清掉残留需卸载后重装一次 |
| 「清除标记」把用户原有的 `InfoTip` / `IconArea_Image` 一起删了 | 清除时整段 `[.ShellClassInfo]` 被摘掉 | 只摘本程序写的那三行（`IconResource` / `IconFile` / `IconIndex`），其余原样保留 |
| 「清除标记」把只读文件夹改成可写、把系统文件夹的系统属性抹掉 | 清除时无条件 `& ~(ReadOnly|System)` | 标记前把属性记进 `[FolderMarkerState]`，清除时整份还原 |
| 没有写入权限时"点了没反应" | `SHGetSetFolderCustomSettings` 即使写不进去也可能返回 S_OK，程序又只看返回码 | 改为直接检查 `desktop.ini` 内容是否真的写上了；失败弹提示框并返回 6 |
| 卸载后目录删不掉、留下日志 | 日志写在 `%LOCALAPPDATA%\FolderMarker`，卸载清单清的是 `{app}` | 日志改到 exe 旁边，卸载清单同时兜底清理旧路径 |
| 路径超过 260 字符的文件夹报「不存在」 | 没有 `app.config` 时运行时按 4.0 语义走 legacy 路径逻辑，`Directory.Exists` 对长路径静默返回 false | 显式声明目标框架 4.6.2 + 文件操作统一补 `\\?\` 前缀 |
| 卸载后已标记的文件夹留下 `desktop.ini` 和 `System` 属性 | 程序不记录标记过哪些文件夹，卸载时无从还原 | 记进 `marked.txt`，卸载前跑 `cleanup` 批量还原 |
| Win11 主菜单里怎么都不出现 | 主菜单只加载带包身份的 `IExplorerCommand`，纯注册表（含 `ExplorerCommandHandler`）一律被收进「显示更多选项」 | 改成原生 COM + sparse MSIX 包，见上面「出现在 Windows 11 的主菜单里」 |
| `Add-AppxPackage` 报 `0x800B0109` | AppX 部署服务跑在 SYSTEM 上下文，只认**本机**根存储；证书装进当前用户根存储没用 | 安装器提权后用 `certutil -addstore root` 写本机根 |
| 覆盖安装时新 DLL 没被替换 | 旧的 `FolderMarker.Shell.dll` 被资源管理器（以及剪映这类会枚举 shell 扩展的程序）加载着，写不进去 | 复制前先把旧文件改名成带时间戳的 `.old`，绕开占用 |
| 装完提示「需要重启才能继续安装」并中止 | 上一次安装因文件占用挂了「重启后替换」，注册表 `PendingFileRenameOperations` 里留着条目，Inno 看到就拒绝继续 | 在 `InitializeSetup`（已提权的最早时刻）清掉属于自己的挂起项 |
| 「显示更多选项」里多出一份重复菜单 | 升级时旧的注册表菜单键没删，和新加的 MSIX 菜单并存 | 包注册成功后自动删除旧键（仅 Win11，Win10 仍保留注册表菜单） |
| 安装时报 `Runtime error (at ...): Type Mismatch` | `GetDateTimeString` 的后两个参数是 `Char`，传了空字符串 | 改传 `#0` |

## 已知限制

- **跨用户不共享**：程序文件装在 `%LOCALAPPDATA%\FolderMarker`，右键菜单靠 per-user 注册的
  MSIX 包提供，所以每个用户都要装一次。
- **必须有一次 UAC**：签名证书要写进本机受信任根、应用包要注册，这两件事都需要管理员。
  点一次「是」之后安装全程自动，不会再弹。
- **卸载只还原「本程序标记过」的文件夹**：记录在 `%LOCALAPPDATA%\FolderMarker\marked.txt` 里，
  一行一个路径。这个清单是从 v1.4.1 才开始记的，**更早版本标记过的文件夹不在清单里**，
  卸载时不会被还原（重装一次、重新点一下颜色，之后就会纳入管理）。
- **标记清单不跟着文件夹走**：文件夹被改名/搬走后，清单里的旧路径会失效，
  卸载时按"不存在"跳过；它已经不指向本程序的图标了，跳过是正确的。
- **两台电脑都要装、且都装在默认目录**：`desktop.ini` 用 `%LOCALAPPDATA%` 环境变量，
  另一台没装或装到自定义目录，就会指不到缓存图标而显示成默认黄文件夹。
- **另一台电脑只需导入一次**：新装的机器 `marked.txt` 是空的，守护进程不知道该扫哪里。
  跑一次 `FolderMarker.exe import <共享盘根目录>` 把根记进 `roots.txt` 即可，
  之后新增标记/换色/内容变化全自动跟上（小根 5 分钟扫一次、慢扫描自动 30 分钟，开机立即扫）。
- **扫根的开销取决于根的大小**：只枚举目录并查 `desktop.ini`，不读文件内容，
  但 SMB 上每个目录都是一次网络往返。195 个目录 ≈ 1.2 秒；8407 个目录 ≈ 10.8 秒。
  给最深的那层根最省；程序也会把 >2 秒的扫描自动拉长到 30 分钟一次（实测日志：
  `root scan: 10748ms 新增=0 下次间隔=30分钟`）。
  日常巡检只处理 `marked.txt` 里的文件夹，不递归扫盘，每 60 秒一次、几乎无感。
  扫描都在等网络 I/O，几乎不占 CPU；只有真的重画 ico 时才用 CPU。
- **内容预览是快照，不是 Windows 实时渲染**：它取的是文件夹里第一个图片/视频的缩略图。
  内容变化由守护进程默认每 60 秒巡检发现并重画，所以最长约一分钟延迟；
  开机时会强制全量重画一次，也可以随时手动 `refresh none force`。
- **守护进程是登录自启**：未登录时不巡检；但缓存本身持久，登录前不会掉色。
  它是无窗口的后台程序，任务管理器要到**「详细信息」页**找 `FolderMarker.exe`
  （命令行里带 `watch`），「进程」页可能被折叠进后台进程组。
  想确认它有没有在工作，看 `%LOCALAPPDATA%\FolderMarker\refresh.log` 里的
  `root scan` / `refresh ok` 记录；也可以随时手动跑一次：

  ```
  "%LOCALAPPDATA%\FolderMarker\FolderMarker.exe" watch
  ```

  已有一个实例在跑时，第二个会因为单例锁立刻退出，不会重复占资源。
- **升级后要重新标记一次**：v1.5.12 及更早版本写的是 `icons\<颜色>.ico` 环境变量路径、
  旧的绝对路径或文件夹内相对路径，还可能留了 `FolderMarker.ico`。新版本清除时能识别并
  清掉这些旧标记，重新点一次颜色即可生成缓存 ico、接上自动巡检。
- **预览取的是「第一个」图片/视频**：按文件名排序取第一个能出缩略图的文件
  （与资源管理器一致），不是随机挑；文件夹里没有图片/视频时退化为纯色图标。
- **Win10 只有「显示更多选项」里的级联菜单**：新版主菜单是 Win11 才有的能力，
  Win10（2004 及以上）会自动退回传统注册表菜单。

## 第三方图标来源
- `icons/palette.ico`：基于 [Fluent UI System Icons](https://github.com/microsoft/fluentui-system-icons)
  的 `color-24-regular`，MIT License，Copyright (c) Microsoft Corporation。
- 其余 icons/*.ico 为本项目自绘。
