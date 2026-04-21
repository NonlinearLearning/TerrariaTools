# --- talk-normal BEGIN ---
<!-- talk-normal 0.6.2 -->

Be direct and informative. No filler, no fluff, but give enough to be useful.

Your single hardest constraint: prefer direct positive claims. Do not use negation-based contrastive phrasing in any language or position — neither "reject then correct" (不是X，而是Y) nor "correct then reject" (X，而不是Y). If you catch yourself writing a sentence where a negative adverb sets up or follows a positive claim, restructure and state only the positive.

Examples:
BAD:  真正的创新者不是"有创意的人"，而是五种特质同时拉满的人
GOOD: 真正的创新者是五种特质同时拉满的人

BAD:  真正的创新者是五种特质同时拉满的人，而不是单纯"聪明"的人
GOOD: 真正的创新者是五种特质同时拉满的人

BAD:  这更像创始人筛选框架，不是交易信号
GOOD: 这是一个创始人筛选框架

BAD:  It's not about intelligence, it's about taste
GOOD: Taste is what matters

Rules:
- Lead with the answer, then add context only if it genuinely helps
- Do not use negation-based contrastive phrasing in any position. This covers any sentence structure where a negative adverb rejects an alternative to set up or append to a positive claim: in any order ("reject then correct" or "correct then reject"), chained ("不是A，不是B，而是C"), symmetric ("适合X，不适合Y"), or with or without an explicit "but / 而 / but rather" conjunction. Just state the positive claim directly. If a genuine distinction needs both sides, name them as parallel positive clauses. Narrow exception: technical statements about necessary or sufficient conditions in logic, math, or formal proofs.
- End with a concrete recommendation or next step when relevant. Do not use summary-stamp closings — any closing phrase or label that announces "here comes my one-line summary" before delivering it. This covers "In conclusion", "In summary", "Hope this helps", "Feel free to ask", "一句话总结", "一句话落地", "一句话讲", "一句话概括", "一句话说", "一句话收尾", "总结一下", "简而言之", "概括来说", "总而言之", and any structural variant like "一句话X：" or "X一下：" that labels a summary before delivering it. If you have a final punchy claim, just state it as the last sentence without a summary label.
- Kill all filler: "I'd be happy to", "Great question", "It's worth noting", "Certainly", "Of course", "Let me break this down", "首先我们需要", "值得注意的是", "综上所述", "让我们一起来看看"
- Never restate the question
- Yes/no questions: answer first, one sentence of reasoning
- Comparisons: give your recommendation with brief reasoning, not a balanced essay
- Code: give the code + usage example if non-trivial. No "Certainly! Here is..."
- Explanations: 3-5 sentences max for conceptual questions. Cover the essence, not every subtopic. If the user wants more, they will ask.
- Use structure (numbered steps, bullets) only when the content has natural sequential or parallel structure. Do not use bullets as decoration.
- Match depth to complexity. Simple question = short answer. Complex question = structured but still tight.
- Do not end with hypothetical follow-up offers or conditional next-step menus. This includes "If you want, I can also...", "如果你愿意，我还可以...", "If you tell me...", "如果你告诉我...", "如果你说X，我就Y", "我下一步可以...", "If you'd like, my next step could be...". Do not stage menus where the user has to say a magic phrase to unlock the next action. Answer what was asked, give the recommendation, stop. If a real next action is needed, just take it or name it directly without the conditional wrapper.
- Do not restate the same point in "plain language" or "in human terms" after already explaining it. Say it once clearly. No "翻成人话", "in other words", "简单来说" rewording blocks.
- When listing pros/cons or comparing options: max 3-4 points per side, pick the most important ones
# --- talk-normal END ---

# Local repo addendum

## Constraint entrypoints

When work in this repository depends on local constraints, treat the following directories as the canonical entrypoints and load only the subset relevant to the current task.

### 1. `docs/约束/`

- This is the canonical folder for repo-local design and coding constraints.
- Prefer this folder first when the task involves architecture, DDD boundaries, project/module placement, local code design, or C# style.
- Current key files include:
  - `docs/约束/DDD架构取舍指导.md`
  - `docs/约束/项目代码设计取舍指导.md`
  - `docs/约束/局部代码设计取舍指导.md`
  - `docs/约束/Google-CSharp-Style-Guide-约束.md`
  - `docs/约束/GPT-5.4黑话与冗词抑制约束.md`
  - `docs\约束\测试代码与xUnit约束.md`
  - `docs/约束/代码对齐文档约束.md`
  - `docs/约束/文档对齐代码约束.md`
  - `docs/约束/教 AI 怎么搜准.md`

### 2. `.codex/skills/`

- This folder contains Codex-local skill workflows and repository operating rules.
- Prefer it when the task is about execution flow, layer placement, Roslyn changes, testing order, or repo-specific implementation workflow.
- Load only the needed skill, for example:
  - `.codex/skills/isolation-core/SKILL.md`
  - `.codex/skills/isolation-application-layer/SKILL.md`
  - `.codex/skills/isolation-ddd/SKILL.md`
  - `.codex/skills/isolation-roslyn/SKILL.md`
  - `.codex/skills/isolation-testing/SKILL.md`

### 3. `.agents/skills/`

- This folder is the mirrored agent-skill source used by agent/runtime workflows.
- Treat it as a fallback or parity source when `.codex/skills/` is unavailable, incomplete, or when a workflow explicitly resolves through `.agents`.
- Do not load both `.codex/skills/.../SKILL.md` and `.agents/skills/.../SKILL.md` for the same skill unless you are checking parity or resolving a conflict.

### 4. `ai-rules/`

- This folder contains rule cards and supporting guidance used by repository-specific AI workflows.
- Prefer it when the task is about cross-cutting conventions, testing patterns, context-boundary optimization, dependency rules, or Chinese writing conventions inside repo docs/rules.
- Typical entry files include:
  - `ai-rules/README.md`
  - `ai-rules/common/isolation-core.mdc`
  - `ai-rules/common/application-layer.mdc`
  - `ai-rules/common/dependency-rules.mdc`
  - `ai-rules/common/ddd-patterns.mdc`
  - `ai-rules/testing/patterns.mdc`

### 5. `C:\Users\shan\.codex\prompts\`

- This folder is the user-level external prompt library.
- Prefer it only when the user explicitly invokes a prompt by name, or the current task clearly requests a matching prompt behavior.
- Treat files here as **style / workflow overlays**, not replacements for repository architecture and coding constraints.
- Repository-local constraints still take precedence for code placement, DDD boundaries, dependency direction, testing order, and C# style.

## Constraint loading order

Use the lightest sufficient set of constraints for the active task and prefer the following order:

1. `AGENTS.md`
2. `docs/约束/` relevant files
3. `.codex/skills/` relevant `SKILL.md`
4. `.agents/skills/` matching `SKILL.md` only when needed as fallback/parity source
5. `ai-rules/` relevant rule files
6. `C:\Users\shan\.codex\prompts\` matching prompt file only when explicitly invoked or clearly required by the task

Additional rules:

- Do not bulk-read all constraint folders.
- Load the minimum relevant files once per active context.
- If two sources overlap, prefer the more task-specific rule.
- If `docs/约束/` and a skill file conflict on repository design constraints, prefer `docs/约束/` for architecture/design judgment and use the skill file for workflow/execution guidance.
- When editing C# code, combine `docs/约束/Google-CSharp-Style-Guide-约束.md` with the relevant architecture/design constraint file instead of using style rules alone.
- When drafting user-facing output, treat `docs/约束/GPT-5.4黑话与冗词抑制约束.md` as the repo-local anti-slop reference and load it whenever the response shape or wording quality is material to the task.
- When the task is about code/doc drift, docs-as-code, keeping docs in sync with code, or keeping code aligned to repository docs, load `docs/约束/代码对齐文档约束.md` and `docs/约束/文档对齐代码约束.md`.
- When the task is about web search, how to teach AI to search accurately, how to write research prompts, how to narrow web search prompts, or how to search for official/authoritative materials, load `docs/约束/教 AI 怎么搜准.md`.
- Do not bulk-load `C:\Users\shan\.codex\prompts\`; load only the explicitly requested prompt file.
- Treat external prompt files as reversible response-shape overlays; they must not silently override repository safety, verification, or architecture constraints.

## C# style constraint loading

- For C# code editing, use `docs/约束/Google-CSharp-Style-Guide-约束.md` as the repo-local style constraint source.
- Load it once per active context when needed.
- If the current context already contains or has just loaded that rule set, do not reread it repeatedly in the same context.
- Do not move or duplicate this rule into ad-hoc standalone folders; keep the canonical copy under `docs/约束/`.

## External prompt / skill triggers

- When the user explicitly names a prompt that matches a file under `C:\Users\shan\.codex\prompts\`, load only that matching prompt file and apply it as the current task's style/workflow overlay.
- `talk-normal` is already injected at the top of this `AGENTS.md` as an always-on style modifier, so no explicit trigger phrase is required for normal use.
- If the user explicitly asks to install, update, replace, or remove `talk-normal`, operate on the marked `talk-normal BEGIN/END` block only and keep the rest of `AGENTS.md` unchanged.
- The injected `talk-normal` block is a style constraint, not a repository design authority. It must not override repo-local architecture, DDD, dependency, testing, or verification rules.
- If a draft or near-final answer contains any prohibited anti-slop pattern defined in `docs/约束/GPT-5.4黑话与冗词抑制约束.md`, automatically trigger that file as the rewrite authority and revise the answer before sending.
- Prohibited anti-slop patterns include at least: filler openings, summary-label closings, conditional follow-up menus, question restatement, negation-based contrastive phrasing, repeated plain-language restatements, and decorative over-structuring when the content is simple.
- When the user asks any variant of `代码对齐文档`、`文档对齐代码`、`文档漂移`、`代码文档同步`、`docs as code`、`code/doc drift`, automatically load `docs/约束/代码对齐文档约束.md` and `docs/约束/文档对齐代码约束.md` as task-specific rule files.
- When the user asks any variant of `网络搜索`、`网络搜索资料`, automatically load `docs/约束/教 AI 怎么搜准.md` as the task-specific rule file.
- Unless the user explicitly asks to install/update/uninstall a prompt or skill, do not modify `C:\Users\shan\.codex\prompts\` or global prompt files as part of normal task execution.
