const repo = "https://github.com/himomohi/codex-pet-hud";
const releasePage = `${repo}/releases`;
const copy = {
  en: {
    navStory:"Story",navDownload:"Download",navChangelog:"Changelog",heroTitle:"See your Codex usage.<br><em>At a glance.</em>",heroBody:"Five-hour and weekly limits become clear, glanceable potions beside your Codex pet.",downloadLatest:"Download preview",meetHud:"Meet the HUD ↓",checkingRelease:"Checking latest release…",storyTitle:"It follows your pet,<br>not your keyboard.",storyBody:"When the Codex pet disappears, the potions and network refresh disappear with it. Bring the pet back and the HUD returns automatically.",featureNative:"Native Swift/AppKit and .NET 8/WPF",featurePrivacy:"No keyboard capture. No token logging.",featureCenter:"The original pet stays centered and clickable.",downloadTitle:"Pick your potion pack.",downloadBody:"Every preview includes source-linked builds and a SHA-256 checksum. No administrator access is required.",macRequirement:"macOS 13+ · Apple Silicon + Intel",windowsRequirement:"Windows 10/11 · x64",windowsArmRequirement:"Windows 11 · ARM64",verifyDownload:"Verify your download",previewWarningTitle:"Preview warning",previewWarning:"These builds are not yet notarized or code-signed. Gatekeeper or SmartScreen may show an unknown-developer warning. Download only from the official GitHub Releases page.",whatsNew:"What’s new",releaseFallback:"See the latest categorized changes on GitHub.",addedTitle:"✨ Features",changedTitle:"🔧 Improvements",fixedTitle:"🐛 Bug fixes",securityTitle:"🔒 Security",fullChangelog:"Read the full changelog →",finalEyebrow:"A tiny companion for long coding sessions.",finalTitle:"Give your pet<br>a pair of potions.",chooseDownload:"Choose a download",viewSource:"View source",communityNotice:"Independent community project · Not an official OpenAI product",latest:"Latest preview"
  },
  ko: {
    navStory:"소개",navDownload:"다운로드",navChangelog:"변경 내역",heroTitle:"Codex<br>사용량을<br><em>눈으로 확인.</em>",heroBody:"5시간·주간 한도를 Codex 펫 곁의 포션으로 한눈에 확인하세요.",downloadLatest:"프리뷰 다운로드",meetHud:"HUD 만나보기 ↓",checkingRelease:"최신 릴리스 확인 중…",storyTitle:"키보드가 아닌,<br>펫을 따라갑니다.",storyBody:"Codex 펫이 사라지면 포션과 네트워크 갱신도 함께 멈춥니다. 펫을 다시 켜면 HUD가 자동으로 돌아옵니다.",featureNative:"macOS Swift/AppKit · Windows .NET 8/WPF 네이티브",featurePrivacy:"키보드 입력 없음 · 토큰 로그 없음",featureCenter:"기존 펫은 중앙에 그대로, 클릭도 그대로.",downloadTitle:"내 포션 팩 고르기.",downloadBody:"모든 프리뷰에는 소스 기반 빌드와 SHA-256 체크섬이 포함됩니다. 관리자 권한은 필요하지 않습니다.",macRequirement:"macOS 13+ · Apple Silicon + Intel",windowsRequirement:"Windows 10/11 · x64",windowsArmRequirement:"Windows 11 · ARM64",verifyDownload:"다운로드 파일 검증",previewWarningTitle:"프리뷰 주의",previewWarning:"현재 빌드는 공증 및 코드 서명이 완료되지 않았습니다. Gatekeeper 또는 SmartScreen 경고가 나타날 수 있으므로 공식 GitHub Releases에서만 다운로드하세요.",whatsNew:"변경 사항",releaseFallback:"최신 분류별 변경 사항은 GitHub에서 확인하세요.",addedTitle:"✨ 새로운 기능",changedTitle:"🔧 개선 사항",fixedTitle:"🐛 버그 수정",securityTitle:"🔒 보안",fullChangelog:"전체 변경 내역 보기 →",finalEyebrow:"긴 코딩 시간을 위한 작은 동료.",finalTitle:"당신의 펫에게<br>포션 한 쌍을.",chooseDownload:"다운로드 선택",viewSource:"소스 보기",communityNotice:"독립 커뮤니티 프로젝트 · OpenAI 공식 제품이 아닙니다",latest:"최신 프리뷰"
  },
  zh: {
    navStory:"介绍",navDownload:"下载",navChangelog:"更新日志",heroTitle:"一眼看清<br><em>Codex 用量。</em>",heroBody:"五小时和每周限额化作 Codex 宠物旁一眼可读的药水瓶。",downloadLatest:"下载预览版",meetHud:"认识 HUD ↓",checkingRelease:"正在检查最新版本…",storyTitle:"跟随你的宠物，<br>而不是键盘。",storyBody:"Codex 宠物隐藏时，药水和网络刷新也会停止；宠物重新显示后，HUD 会自动恢复。",featureNative:"原生 Swift/AppKit 与 .NET 8/WPF",featurePrivacy:"不采集键盘输入，不记录令牌",featureCenter:"原始宠物保持居中并可点击。",downloadTitle:"选择你的药水包。",downloadBody:"每个预览版都包含可追溯源码的构建和 SHA-256 校验值，无需管理员权限。",macRequirement:"macOS 13+ · Apple Silicon + Intel",windowsRequirement:"Windows 10/11 · x64",windowsArmRequirement:"Windows 11 · ARM64",verifyDownload:"验证下载文件",previewWarningTitle:"预览版提示",previewWarning:"当前构建尚未完成公证或代码签名，Gatekeeper 或 SmartScreen 可能发出警告。请仅从官方 GitHub Releases 下载。",whatsNew:"更新内容",releaseFallback:"请在 GitHub 查看最新的分类更新。",addedTitle:"✨ 新功能",changedTitle:"🔧 改进",fixedTitle:"🐛 错误修复",securityTitle:"🔒 安全",fullChangelog:"查看完整更新日志 →",finalEyebrow:"长时间编码的小伙伴。",finalTitle:"送给你的宠物<br>一对药水。",chooseDownload:"选择下载",viewSource:"查看源码",communityNotice:"独立社区项目 · 非 OpenAI 官方产品",latest:"最新预览版"
  },
  ja: {
    navStory:"紹介",navDownload:"ダウンロード",navChangelog:"変更履歴",heroTitle:"Codex の使用量を<br><em>ひと目で確認。</em>",heroBody:"5時間枠と週間枠を、Codex ペットの隣のポーションでひと目で確認できます。",downloadLatest:"プレビューをダウンロード",meetHud:"HUDを見る ↓",checkingRelease:"最新リリースを確認中…",storyTitle:"キーボードではなく、<br>ペットについていく。",storyBody:"Codex ペットが消えると、ポーションとネットワーク更新も停止します。ペットを戻すと HUD も自動で復帰します。",featureNative:"Swift/AppKit と .NET 8/WPF のネイティブ実装",featurePrivacy:"キーボード取得なし・トークン記録なし",featureCenter:"元のペットは中央に残り、クリックできます。",downloadTitle:"ポーションパックを選ぶ。",downloadBody:"各プレビューにはソースに対応したビルドと SHA-256 チェックサムが含まれ、管理者権限は不要です。",macRequirement:"macOS 13+ · Apple Silicon + Intel",windowsRequirement:"Windows 10/11 · x64",windowsArmRequirement:"Windows 11 · ARM64",verifyDownload:"ダウンロードを検証",previewWarningTitle:"プレビューの注意",previewWarning:"現在のビルドは公証・コード署名が未完了です。Gatekeeper や SmartScreen の警告が表示される場合があります。公式 GitHub Releases からのみ取得してください。",whatsNew:"変更点",releaseFallback:"最新の分類済み変更点は GitHub で確認できます。",addedTitle:"✨ 新機能",changedTitle:"🔧 改善",fixedTitle:"🐛 バグ修正",securityTitle:"🔒 セキュリティ",fullChangelog:"変更履歴をすべて見る →",finalEyebrow:"長いコーディング時間の小さな相棒。",finalTitle:"あなたのペットに<br>2つのポーションを。",chooseDownload:"ダウンロードを選ぶ",viewSource:"ソースを見る",communityNotice:"独立コミュニティプロジェクト · OpenAI 公式製品ではありません",latest:"最新プレビュー"
  }
};

let currentRelease;

function selectedCopy(){
  return copy[localStorage.getItem("language") || "en"] || copy.en;
}

function parseChanges(body){
  const headingKeys = {"✨ Features":"addedTitle","🔧 Improvements":"changedTitle","🐛 Bug fixes":"fixedTitle","🔒 Security":"securityTitle"};
  const groups = [];
  let current;
  for(const line of (body || "").split("\n")){
    if(line === "Download the archive for your platform and verify it with `SHA256SUMS.txt`.") break;
    const heading = line.match(/^### (.+)$/);
    if(heading){
      const key = headingKeys[heading[1]];
      current = key ? {key, items:[]} : undefined;
      if(current) groups.push(current);
      continue;
    }
    const item = line.match(/^- (.+)$/);
    if(current && item) current.items.push(item[1]);
  }
  return groups.filter(group => group.items.length);
}

function renderRelease(release){
  const strings = selectedCopy();
  document.querySelector("#releaseHeading").textContent = release ? `${strings.whatsNew} · ${release.tag_name}` : strings.whatsNew;
  if(!release) return;

  const releaseDate = new Date(release.published_at).toLocaleDateString(document.documentElement.lang, {year:"numeric",month:"2-digit",day:"2-digit"});
  document.querySelector("#heroVersion").textContent = `${strings.latest} · ${release.tag_name}`;
  document.querySelector("#releaseVersion").textContent = release.tag_name;
  document.querySelector("#releaseDate").textContent = releaseDate;
  document.querySelector("#changelogDate").textContent = releaseDate;
  const container = document.querySelector("#releaseChanges");
  const groups = parseChanges(release.body);
  container.replaceChildren();
  if(!groups.length){
    const fallback = document.createElement("p");
    fallback.textContent = strings.releaseFallback;
    container.append(fallback);
    return;
  }
  for(const group of groups){
    const heading = document.createElement("h3");
    heading.textContent = strings[group.key];
    const list = document.createElement("ul");
    for(const text of group.items){
      const item = document.createElement("li");
      item.textContent = text;
      list.append(item);
    }
    container.append(heading, list);
  }
}

function setLanguage(lang){
  const selected = copy[lang] ? lang : "en";
  document.documentElement.lang = selected === "zh" ? "zh-CN" : selected;
  localStorage.setItem("language", selected);
  document.querySelectorAll("[data-i18n]").forEach(el => {
    const value = copy[selected][el.dataset.i18n];
    if(value) el.innerHTML = value;
  });
  document.querySelectorAll("[data-lang]").forEach(button => button.classList.toggle("active", button.dataset.lang === selected));
  renderRelease(currentRelease);
}

function preferredLanguage(){
  const saved = localStorage.getItem("language");
  if(saved) return saved;
  const lang = navigator.language.toLowerCase();
  if(lang.startsWith("ko")) return "ko";
  if(lang.startsWith("zh")) return "zh";
  if(lang.startsWith("ja")) return "ja";
  return "en";
}

function assetLink(release, pattern){
  return release.assets.find(asset => pattern.test(asset.name))?.browser_download_url || releasePage;
}

async function loadRelease(){
  try{
    const response = await fetch("https://api.github.com/repos/himomohi/codex-pet-hud/releases/latest", {headers:{Accept:"application/vnd.github+json"}});
    if(!response.ok) throw new Error(response.status);
    const release = await response.json();
    currentRelease = release;
    renderRelease(release);

    const links = {mac:assetLink(release,/macOS-universal\.zip$/i),x64:assetLink(release,/Windows-x64\.zip$/i),arm:assetLink(release,/Windows-arm64\.zip$/i),sum:assetLink(release,/SHA256SUMS\.txt$/i)};
    document.querySelector("#macDownload").href = links.mac;
    document.querySelector("#winX64Download").href = links.x64;
    document.querySelector("#winArmDownload").href = links.arm;
    document.querySelector("#checksumDownload").href = links.sum;

    const isWindows = navigator.userAgent.includes("Windows");
    const isArm = navigator.userAgent.includes("ARM64");
    document.querySelector("#heroDownload").href = isWindows ? (isArm ? links.arm : links.x64) : links.mac;
    if(isWindows){
      document.querySelector("#macDownload").classList.remove("recommended");
      document.querySelector(isArm ? "#winArmDownload" : "#winX64Download").classList.add("recommended");
    }
  }catch(_){
    document.querySelector("#heroVersion").textContent = "GitHub Releases";
  }
}

document.querySelectorAll("[data-lang]").forEach(button => button.addEventListener("click", () => setLanguage(button.dataset.lang)));
setLanguage(preferredLanguage());
loadRelease();
