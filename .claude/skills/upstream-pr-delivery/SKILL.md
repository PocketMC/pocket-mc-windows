---
name: upstream-pr-delivery
description: 把 fork 中的多项改动整合成上游可评审的单个 Draft PR：按子系统拆分可独立编译的提交、剔除发布噪音文件、逐提交构建验证、编写中英双语说明并关联 fork 发布构建。适用于向开源上游提交较大改动、被审阅者要求 “one draft PR with proper commit history” 时。
---

# 上游规范 PR 交付

## 适用场景

- 改动较大（多子系统、上百文件），此前分成多个 PR 被要求合并。
- 审阅者要求：单一 Draft PR、规范的提交历史、可直接下载的构建产物。
- fork 自带版本号/CHANGELOG/发行说明，但上游不需要这些差异。

## 核心原则

1. **一个 PR 只讲一件事的集合**：同一主题的改动进同一 PR，其余全部 close 并留 superseded 说明。
2. **每个提交单独可编译**：审阅者会逐提交 build；“最终态可编译”不等于提交序列规范。
3. **提交按子系统切分**，不按文件数量切分；一次机械替换（如全量文案抽取）作为独立提交，不再硬拆。
4. **仓库专属文件不入 PR**：版本号、CHANGELOG、发行说明、构建脚本、本地 .gitignore。
5. **说明先行**：PR 正文给出改动概览、提交表、验证命令与结果、可下载构建链接。
6. **纯英文产出**：PR 标题、正文、提交信息、评审回复一律英文；中文只用于 release 双语文案、仓库内文档或与用户对话。正文若已含中文摘要，用 `gh pr edit --body-file` 覆盖删除。
7. **持续推送同一 PR**：后续改动继续 push 到该 Draft PR，不新开。

## 步骤

### 1. 建基线分支

```bash
git fetch upstream master
git checkout -B feat/<topic> upstream/master
```

不要从自己的 master 分支直接开，避免把 fork 的版本提交带进 PR。

### 2. 按子系统分组提交

先列出 `git diff --name-only upstream/master..master`，再按目录/职责分组（settings → 服务层 → UI 接线 → 文案），每组一个提交。

```bash
git checkout master -- <path...>     # 从工作分支取文件
git add -A
git commit -F - <<'EOF'
feat(downloads): add mirror-aware and segmented downloads

Route downloads through mirrors with failure cooldowns and split large
files into parallel ranges with configurable tuning.
EOF
```

**排除清单**（按项目调整）：`pocketmc.yml` 的版本行、`CHANGELOG.md`、`WhatsNew.txt`、`build.bat`、`run.bat`、`.gitignore`。版本行要用上游值覆盖：

```bash
git show upstream/master:pocketmc.yml | grep '^version:'
# 把工作区的 version 行改回上游值，只保留新增配置段
```

### 3. 逐个提交构建验证（关键）

```bash
for c in $(git log --format=%h --reverse upstream/master..HEAD); do
  git checkout -q "$c"
  printf '%s ' "$c"; git log -1 --format=%s
  dotnet build PocketMC.Desktop.sln 2>&1 | grep -E "个错误|error " | head -2
done
git checkout -q feat/<topic>
```

常见失败：csproj 在早期提交就注册了后期才加入的资源文件（图片/字体/图标），报 `BG1002 无法找到文件`。修法是把资源注册与资源文件放进同一提交，或早期提交里删掉该行、最后提交再恢复。

### 4. 处理噪音差异

- CRLF/LF 全量改写：若某个提交把整个文件换成 CRLF，把文件归一化为 LF 并放置到真正改动的提交里，避免 `3000 行改动` 的假差异。
- 大重构文件：无法再拆就保持整文件，但要在提交说明里写清楚。

### 5. 跑全量测试

```bash
dotnet build PocketMC.Desktop.sln
dotnet test PocketMC.Desktop.sln
```

把测试数量写进 PR 正文，作为“可评审”的证据。

### 6. 开 Draft PR

```bash
git push -u origin feat/<topic>
gh pr create --repo <org>/<repo> --base master \
  --head <user>:feat/<topic> --draft \
  --title "feat: <一句话概括>" --body-file pr-body.md
```

正文结构（纯英文）：Review builds → Commit history（表格：提交短哈希 + 职责）→ Summary → 各子系统细节 → Testing。

### 7. 关闭旧 PR 并说明

```bash
gh pr comment 112 --repo <org>/<repo> --body "Superseded by #114, which consolidates all changes into a single draft PR with a split commit history."
gh pr close 112 --repo <org>/<repo>
```

### 8. 持续更新

后续改动继续提交到同一分支；提交历史被重写时用 `git push -f`，GitHub 会自动更新 PR。保持 Draft 直到审阅者要求转正。

### 9. 每个主要改动发布可下载构建

审阅者常要求“直接从 release 下载验证”：

```bash
dotnet publish -c Release -r win-x64 --self-contained false -p:Version=<v> -o publish
vpk pack -u PocketMC -v <v> -p publish -e PocketMC.exe --framework net8.0 --runtime win-x64 -o Releases
gh release create v<v> --repo <user>/<repo> --title "PocketMC v<v>" --notes-file rel.md \
  Releases/PocketMC-win-Setup.exe Releases/PocketMC-win-Portable.zip Releases/*.nupkg
```

PR 正文里链接最新 release，注明 Setup/Portable 两个下载入口。

## 自查清单

- [ ] 基于 `upstream/master`，不是 fork master
- [ ] 每个提交单独 `dotnet build` 通过（脚本输出留证）
- [ ] HEAD 全量测试通过，数字写进正文
- [ ] 版本号/CHANGELOG/发行说明/构建脚本未入 PR
- [ ] 提交信息常规（`feat|fix|chore(scope): 主题`，正文 72 列换行）
- [ ] PR 为 Draft，正文含 Review builds 与提交表
- [ ] 旧 PR 已关闭并留 superseded 说明
- [ ] 最新 release 与 PR HEAD 内容一致
- [ ] PR 标题/正文/提交信息/回复无中文（ 无输出）

## 反例（真实踩坑）

- 只验证 HEAD 可编译 → 中间 4 个提交全部 `BG1002 资源文件缺失`，被质疑“proper commit history”是否成立。
- 用 `git commit -a` 一次性提交 6 个文件 → 历史只有一个 `update`，无法拆分评审。
- 把 fork 的版本号行带进 PR → 上游合并后版本冲突。
- 忘记关闭旧 PR → 审阅者需要重复对照两套差异。
