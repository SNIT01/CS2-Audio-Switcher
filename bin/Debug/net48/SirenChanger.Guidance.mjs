/*
 * Cities: Skylines II UI Module
 *
 * Id: SirenChanger.Guidance
 * Author: SNIT01
 * Version: 1.0.0
 * Dependencies:
 */

const React = window.React;
const ReactDOM = window.ReactDOM;
const api = window["cs2/api"];
const ui = window["cs2/ui"];

const GROUP = "sirenChangerGuidance";
const STYLE_ID = "siren-changer-guidance-style";
const PORTAL_HOST_ID = "siren-changer-guidance-portal-host";

const tutorialVisibleBinding = api.bindValue(GROUP, "tutorialVisible", false);
const changelogVisibleBinding = api.bindValue(GROUP, "changelogVisible", false);
const tutorialTitleBinding = api.bindValue(GROUP, "tutorialTitle", "Audio Switcher Quick Start");
const tutorialBodyBinding = api.bindValue(GROUP, "tutorialBody", "");
const tutorialPagesBinding = api.bindValue(GROUP, "tutorialPages", "");
const changelogTitleBinding = api.bindValue(GROUP, "changelogTitle", "What's New");
const changelogVersionBinding = api.bindValue(GROUP, "changelogVersion", "");
const changelogBodyBinding = api.bindValue(GROUP, "changelogBody", "");
const changelogReleasesBinding = api.bindValue(GROUP, "changelogReleases", "");

function resolveModuleBaseFromImportMeta() {
	try {
		const metaUrl = getImportMetaUrl();
		return resolveBaseUrl(metaUrl);
	}
	catch {
		return "";
	}
}

function getImportMetaUrl() {
	try {
		return String(import.meta && import.meta.url ? import.meta.url : "");
	}
	catch {
		return "";
	}
}

function resolveBaseUrl(inputUrl) {
	const source = String(inputUrl || "").trim();
	if (source.length === 0) {
		return "";
	}

	let end = source.length;
	const queryIdx = source.indexOf("?");
	if (queryIdx >= 0) {
		end = Math.min(end, queryIdx);
	}

	const hashIdx = source.indexOf("#");
	if (hashIdx >= 0) {
		end = Math.min(end, hashIdx);
	}

	const clean = source.substring(0, end);
	const slashIdx = clean.lastIndexOf("/");
	if (slashIdx < 0) {
		return "";
	}

	return clean.substring(0, slashIdx + 1);
}

function resolveModuleBaseFromScriptTag() {
	if (typeof document === "undefined") {
		return "";
	}

	try {
		const scriptUrl = resolveModuleScriptUrlFromScriptTag();
		return resolveBaseUrl(scriptUrl);
	}
	catch {
		return "";
	}
}

function resolveModuleScriptUrlFromScriptTag() {
	if (typeof document === "undefined") {
		return "";
	}

	try {
		const scripts = document.getElementsByTagName("script");
		for (let i = scripts.length - 1; i >= 0; i--) {
			const src = String(scripts[i] && scripts[i].src ? scripts[i].src : "");
			if (src.includes(".Guidance") || src.includes("Guidance.mjs")) {
				return src;
			}
		}

		const currentScriptSrc = String(document.currentScript && document.currentScript.src ? document.currentScript.src : "");
		if (currentScriptSrc.length > 0) {
			return currentScriptSrc;
		}
	}
	catch {
		return "";
	}

	return "";
}

function normalizeTrailingSlash(url) {
	const candidate = String(url || "").trim();
	if (!candidate) {
		return "";
	}

	return candidate.endsWith("/") ? candidate : (candidate + "/");
}

function addBaseCandidate(baseCandidates, baseUrl) {
	const normalized = normalizeTrailingSlash(baseUrl);
	if (!normalized) {
		return;
	}

	addUniqueUrl(baseCandidates, normalized);
}

function addBaseCandidateWithParents(baseCandidates, baseUrl) {
	const normalized = normalizeTrailingSlash(baseUrl);
	if (!normalized) {
		return;
	}

	addBaseCandidate(baseCandidates, normalized);
	try {
		addBaseCandidate(baseCandidates, new URL("../", normalized).href);
	}
	catch {
		// Ignore invalid parent path.
	}

	try {
		addBaseCandidate(baseCandidates, new URL("../../", normalized).href);
	}
	catch {
		// Ignore invalid grandparent path.
	}
}

function extractUiModRootHint(moduleUrl) {
	const clean = String(moduleUrl || "").trim();
	if (!clean) {
		return "";
	}

	const marker = "coui://ui-mods/";
	const markerIdx = clean.toLowerCase().indexOf(marker);
	if (markerIdx < 0) {
		return "";
	}

	const remainder = clean.substring(markerIdx + marker.length).split(/[?#]/)[0];
	if (!remainder) {
		return "";
	}

	const firstSlash = remainder.indexOf("/");
	if (firstSlash > 0) {
		return remainder.substring(0, firstSlash).trim();
	}

	const fileName = remainder.trim();
	if (!fileName.toLowerCase().endsWith(".mjs")) {
		return "";
	}

	const stem = fileName.substring(0, fileName.length - 4);
	const firstDot = stem.indexOf(".");
	if (firstDot > 0) {
		return stem.substring(0, firstDot).trim();
	}

	return stem;
}

function addUniqueUrl(urls, url) {
	const candidate = String(url || "").trim();
	if (!candidate) {
		return;
	}

	// Avoid file:// paths in game UI resources; Gameface expects coui:// or assetdb://.
	if (candidate.startsWith("file://")) {
		return;
	}

	if (!urls.includes(candidate)) {
		urls.push(candidate);
	}
}

function mergeUniqueCandidates(...lists) {
	const merged = [];
	for (const list of lists) {
		if (!Array.isArray(list)) {
			continue;
		}

		for (const value of list) {
			const candidate = String(value || "").trim();
			if (candidate.length > 0 && !merged.includes(candidate)) {
				merged.push(candidate);
			}
		}
	}

	return merged;
}

const preloadedImageSources = new Set();
const resolvedImageSourceByCandidateKey = Object.create(null);

function buildCandidateKey(candidates) {
	if (!Array.isArray(candidates) || candidates.length === 0) {
		return "";
	}

	return candidates.join("|");
}

function preloadImageSourcesIfNeeded(sources) {
	if (typeof Image === "undefined" || !Array.isArray(sources)) {
		return;
	}

	for (const source of sources) {
		const normalized = String(source || "").trim();
		if (!normalized || preloadedImageSources.has(normalized)) {
			continue;
		}

		preloadedImageSources.add(normalized);
		try {
			const image = new Image();
			image.decoding = "async";
			image.src = normalized;
		}
		catch {
			// Ignore preloading failures; regular render fallback will handle them.
		}
	}
}

function buildAssetUrlCandidates(relativePath) {
	const cleaned = String(relativePath || "").replace(/\\/g, "/").replace(/^\/+/, "");
	if (!cleaned) {
		return [];
	}

	const pathVariants = [cleaned];
	if (cleaned.startsWith("Images/")) {
		pathVariants.push("images/" + cleaned.substring("Images/".length));
	}
	else if (cleaned.startsWith("images/")) {
		pathVariants.push("Images/" + cleaned.substring("images/".length));
	}

	const importMetaBase = resolveModuleBaseFromImportMeta();
	const scriptBase = resolveModuleBaseFromScriptTag();
	const importMetaUrl = getImportMetaUrl();
	const scriptModuleUrl = resolveModuleScriptUrlFromScriptTag();
	const baseCandidates = [];
	addBaseCandidateWithParents(baseCandidates, importMetaBase);
	addBaseCandidateWithParents(baseCandidates, scriptBase);

	const modRootHints = [];
	const addRootHint = (value) => {
		const normalized = String(value || "").trim();
		if (!normalized || modRootHints.includes(normalized)) {
			return;
		}
		modRootHints.push(normalized);
	};

	addRootHint(extractUiModRootHint(importMetaUrl));
	addRootHint(extractUiModRootHint(scriptModuleUrl));

	for (const rootHint of modRootHints) {
		addBaseCandidate(baseCandidates, "coui://ui-mods/" + rootHint + "/");
		addBaseCandidate(baseCandidates, "coui://ui-mods/" + rootHint.toLowerCase() + "/");
	}

	addBaseCandidate(baseCandidates, "coui://ui-mods/");

	const urls = [];
	for (const pathVariant of pathVariants) {
		for (const base of baseCandidates) {
			try {
				addUniqueUrl(urls, new URL(pathVariant, base).href);
			}
			catch {
				// Ignore invalid candidate.
			}
		}

		addUniqueUrl(urls, pathVariant);
	}

	return urls;
}

function FallbackImage(props) {
	const { srcCandidates, className, alt, fallback } = props;
	const normalizedCandidates = React.useMemo(() => {
		if (!Array.isArray(srcCandidates)) {
			return [];
		}

		const normalized = [];
		for (const candidate of srcCandidates) {
			const value = String(candidate || "").trim();
			if (value.length > 0 && !normalized.includes(value)) {
				normalized.push(value);
			}
		}
		return normalized;
	}, [srcCandidates]);
	const candidateKey = React.useMemo(() => buildCandidateKey(normalizedCandidates), [normalizedCandidates]);
	const cachedSourceIndex = React.useMemo(() => {
		if (!candidateKey) {
			return 0;
		}

		const cachedSource = String(resolvedImageSourceByCandidateKey[candidateKey] || "").trim();
		if (!cachedSource) {
			return 0;
		}

		const index = normalizedCandidates.indexOf(cachedSource);
		return index >= 0 ? index : 0;
	}, [candidateKey, normalizedCandidates]);
	const [activeIndex, setActiveIndex] = React.useState(cachedSourceIndex);

	React.useEffect(() => {
		setActiveIndex(cachedSourceIndex);
	}, [cachedSourceIndex, candidateKey]);

	React.useEffect(() => {
		preloadImageSourcesIfNeeded(normalizedCandidates);
	}, [candidateKey, normalizedCandidates]);

	if (normalizedCandidates.length === 0 || activeIndex >= normalizedCandidates.length) {
		return fallback || null;
	}

	const activeSource = normalizedCandidates[activeIndex];
	const onError = () => {
		setActiveIndex((index) => index + 1);
	};
	const onLoad = () => {
		if (candidateKey && activeSource) {
			resolvedImageSourceByCandidateKey[candidateKey] = activeSource;
		}
	};

	return React.createElement("img", {
		className,
		src: activeSource,
		alt: alt || "",
		loading: "eager",
		decoding: "async",
		onError,
		onLoad
	});
}

const SHARED_HEADER_BANNER_IMAGE_CANDIDATES = mergeUniqueCandidates(
	buildAssetUrlCandidates("Images/UI_Banner.jpeg"),
	buildAssetUrlCandidates("Images/GuidanceBanner_Tutorial.svg"),
	buildAssetUrlCandidates("Images/GuidanceBanner_Changelog.svg")
);
const TUTORIAL_HEADER_BANNER_IMAGE_CANDIDATES = SHARED_HEADER_BANNER_IMAGE_CANDIDATES;
const CHANGELOG_HEADER_BANNER_IMAGE_CANDIDATES = SHARED_HEADER_BANNER_IMAGE_CANDIDATES;
const DEFAULT_TUTORIAL_IMAGE_CANDIDATES = mergeUniqueCandidates(
	buildAssetUrlCandidates("Images/Screen_Gen.jpg"),
	buildAssetUrlCandidates("Images/Audio Switcher Logo.jpg")
);

const TUTORIAL_TOPIC_ORDER = [
	"What Audio Switcher Covers",
	"City Sound Sets and City Mapping",
	"Sirens: Defaults and Vehicle Overrides",
	"Vehicle Engines",
	"Ambient Sounds",
	"Building Sounds",
	"Public Transport Announcements",
	"Profiles and Advanced Tuning",
	"Missing File Fallback Behavior",
	"Module Builder",
	"PDX Upload Workflow",
	"Developer and Diagnostics"
];

const TUTORIAL_TOPIC_ORDER_MAP = (() => {
	const orderMap = Object.create(null);
	for (let i = 0; i < TUTORIAL_TOPIC_ORDER.length; i++) {
		const key = normalizeTutorialTopicKey(TUTORIAL_TOPIC_ORDER[i]);
		if (key.length > 0) {
			orderMap[key] = i;
		}
	}
	return orderMap;
})();

function normalizeTutorialTopicKey(title) {
	return String(title || "")
		.trim()
		.toLowerCase()
		.replace(/\s+/g, " ");
}

function getTutorialTopicOrderIndex(title) {
	const key = normalizeTutorialTopicKey(title);
	if (key.length === 0) {
		return Number.MAX_SAFE_INTEGER;
	}

	if (Object.prototype.hasOwnProperty.call(TUTORIAL_TOPIC_ORDER_MAP, key)) {
		return Number(TUTORIAL_TOPIC_ORDER_MAP[key]);
	}

	return Number.MAX_SAFE_INTEGER;
}

function sortTutorialPagesByPreferredOrder(pages) {
	const wrapped = pages.map((page, index) => ({ page, index }));
	wrapped.sort((left, right) => {
		const leftOrder = getTutorialTopicOrderIndex(left.page && left.page.title);
		const rightOrder = getTutorialTopicOrderIndex(right.page && right.page.title);
		if (leftOrder !== rightOrder) {
			return leftOrder - rightOrder;
		}

		return left.index - right.index;
	});

	return wrapped.map((entry) => entry.page);
}

function getTutorialNavWindowSize() {
	if (typeof window !== "undefined" && window.innerWidth <= 1280) {
		return 4;
	}

	return 7;
}

function ensurePortalHost() {
	if (typeof document === "undefined" || !document.body) {
		return null;
	}

	let host = document.getElementById(PORTAL_HOST_ID);
	if (!host) {
		host = document.createElement("div");
		host.id = PORTAL_HOST_ID;
		document.body.appendChild(host);
	}

	host.style.position = "fixed";
	host.style.left = "0";
	host.style.top = "0";
	host.style.right = "0";
	host.style.bottom = "0";
	host.style.width = "100vw";
	host.style.height = "100vh";
	host.style.zIndex = "2147482000";
	host.style.pointerEvents = "auto";
	host.style.display = "block";
	host.style.overflow = "visible";

	const refCount = Number(host.getAttribute("data-ref-count") || "0");
	host.setAttribute("data-ref-count", String(refCount + 1));
	return host;
}

function releasePortalHost(host) {
	if (!host) {
		return;
	}

	const refCount = Number(host.getAttribute("data-ref-count") || "1");
	if (refCount <= 1) {
		host.remove();
		return;
	}

	host.setAttribute("data-ref-count", String(refCount - 1));
}

function GuidanceOverlayPortal(props) {
	const { children } = props;
	const [host] = React.useState(() => ensurePortalHost());

	React.useEffect(() => {
		return () => releasePortalHost(host);
	}, [host]);

	if (host && ReactDOM && typeof ReactDOM.createPortal === "function") {
		return ReactDOM.createPortal(children, host);
	}

	const Portal = ui.Portal || React.Fragment;
	return React.createElement(Portal, null, children);
}

function ensureStyles() {
	if (typeof document === "undefined") {
		return;
	}

	let style = document.getElementById(STYLE_ID);
	if (!style) {
		style = document.createElement("style");
		style.id = STYLE_ID;
		document.head.appendChild(style);
	}

	style.textContent = `
		@keyframes sc-guidance-fade-in {
			0% {
				opacity: 0;
				transform: translateY(8rem) scale(0.985);
			}
			100% {
				opacity: 1;
				transform: translateY(0) scale(1);
			}
		}

		.sc-guidance-overlay {
			position: fixed;
			left: 0;
			top: 0;
			right: 0;
			bottom: 0;
			display: flex;
			align-items: center;
			justify-content: center;
			padding: 20rem;
			background: rgba(6, 10, 16, 0.55);
			box-sizing: border-box;
			z-index: 2147480000;
		}

		.sc-guidance-panel {
			width: 1220rem;
			max-width: 92vw;
			max-height: 86vh;
			border-radius: 22rem;
			overflow: hidden;
			--panelRadius: 22rem;
			animation: 220ms sc-guidance-fade-in ease-out forwards;
		}

		.sc-guidance-shell {
			display: flex;
			flex-direction: column;
			background: linear-gradient(165deg, rgba(17, 32, 76, 0.98), rgba(46, 29, 93, 0.96));
			max-height: 86vh;
		}

		.sc-guidance-header {
			position: relative;
			display: flex;
			flex-direction: column;
			padding: 0;
			border-bottom: 1rem solid rgba(255, 255, 255, 0.14);
			background: transparent;
			min-height: 120rem;
			overflow: hidden;
		}

		.sc-guidance-header-banner {
			position: absolute;
			left: 0;
			top: 0;
			right: 0;
			bottom: 0;
			height: auto;
			background: linear-gradient(132deg, rgba(89, 132, 223, 0.94), rgba(96, 74, 198, 0.94));
		}

		.sc-guidance-header-banner-image {
			display: block;
			width: 100%;
			height: 100%;
			opacity: 0.95;
			object-fit: cover;
		}

		.sc-guidance-header-banner-overlay {
			position: absolute;
			left: 0;
			top: 0;
			right: 0;
			bottom: 0;
			background: linear-gradient(145deg, rgba(13, 26, 69, 0.24), rgba(34, 24, 80, 0.52));
		}

		.sc-guidance-header-content {
			display: flex;
			position: relative;
			z-index: 1;
			align-items: center;
			justify-content: space-between;
			gap: 18rem;
			padding: 24rem 28rem 24rem 28rem;
			background: transparent;
		}

		.sc-guidance-header-main {
			display: flex;
			align-items: center;
			gap: 22rem;
			min-width: 0;
		}

		.sc-guidance-title-wrap {
			display: flex;
			flex-direction: column;
			gap: 8rem;
			min-width: 0;
		}

		.sc-guidance-title {
			margin: 0;
			font-size: 44rem;
			line-height: 1.14;
			font-weight: 700;
			color: #f8fbff;
			text-transform: none !important;
			letter-spacing: 0 !important;
			text-shadow: 0 1rem 2rem rgba(14, 24, 53, 0.35);
		}

		.sc-guidance-subtitle {
			margin: 0;
			font-size: 20rem;
			line-height: 1.32;
			color: rgba(237, 246, 255, 0.97);
			text-transform: none !important;
			letter-spacing: 0 !important;
			text-shadow: 0 1rem 2rem rgba(14, 24, 53, 0.28);
		}

		.sc-guidance-close {
			flex: 0 0 auto;
			width: 36rem;
			height: 36rem;
			border-radius: 9rem;
			border: 1rem solid rgba(255, 255, 255, 0.28);
			background: rgba(255, 255, 255, 0.2);
			color: #f7fbff;
			font-size: 24rem;
			line-height: 1;
			font-weight: 700;
			padding: 0;
			display: inline-flex;
			align-items: center;
			justify-content: center;
		}

		.sc-guidance-content {
			padding: 18rem 20rem;
			overflow-y: auto;
			max-height: 620rem;
			display: flex;
			flex-direction: column;
			gap: 12rem;
			color: #edf3fb;
		}

		.sc-guidance-tutorial-layout {
			display: flex;
			align-items: stretch;
		}

		.sc-guidance-tutorial-sidebar {
			flex: 0 0 332rem;
			width: 332rem;
			border: 1rem solid rgba(160, 182, 245, 0.42);
			border-radius: 12rem;
			background: linear-gradient(170deg, rgba(29, 49, 107, 0.64), rgba(57, 38, 112, 0.58));
			padding: 12rem;
			margin-right: 14rem;
			height: 540rem;
			max-height: 540rem;
			overflow: hidden;
			display: block;
			pointer-events: auto;
		}

		.sc-guidance-tutorial-sidebar-title {
			margin: 0;
			font-size: 18rem;
			line-height: 1.3;
			font-weight: 650;
			color: #e8f3ff;
		}

		.sc-guidance-tutorial-sidebar-subtitle {
			margin: 6rem 0 10rem 0;
			font-size: 14rem;
			line-height: 1.35;
			color: rgba(201, 220, 245, 0.93);
		}

		.sc-guidance-step-nav {
			margin: 0;
			padding: 0;
			list-style: none;
		}

		.sc-guidance-step-nav-wrap {
			overflow: hidden;
			padding-bottom: 4rem;
			pointer-events: auto;
		}

		.sc-guidance-step-nav-controls {
			display: flex;
			align-items: center;
			justify-content: space-between;
			gap: 8rem;
		}

		.sc-guidance-step-nav-controls-top {
			margin-bottom: 10rem;
			justify-content: flex-end;
		}

		.sc-guidance-step-nav-controls-bottom {
			margin-top: 10rem;
			padding: 0 4rem 4rem 4rem;
			justify-content: flex-end;
			align-items: center;
			box-sizing: border-box;
		}

		.sc-guidance-step-nav-scroll-button {
			width: 34rem;
			height: 34rem;
			border-radius: 8rem;
			border: 1rem solid rgba(166, 214, 252, 0.76);
			background: linear-gradient(165deg, rgba(44, 89, 162, 0.9), rgba(73, 58, 160, 0.82));
			color: #ebf7ff;
			font-size: 16rem;
			font-weight: 700;
			line-height: 1;
			padding: 0;
			display: inline-flex;
			align-items: center;
			justify-content: center;
			cursor: pointer;
		}

		.sc-guidance-step-nav-scroll-button:disabled {
			opacity: 0.5;
		}

		.sc-guidance-step-nav-window-label {
			flex: 1 1 auto;
			text-align: center;
			font-size: 13rem;
			line-height: 1.3;
			color: rgba(213, 230, 252, 0.95);
			white-space: nowrap;
		}

		.sc-guidance-step-nav-controls-bottom .sc-guidance-step-nav-window-label {
			flex: 0 0 auto;
			text-align: right;
			padding-left: 0;
			margin-right: 10rem;
		}

		.sc-guidance-step-nav-controls-bottom .sc-guidance-step-nav-scroll-button {
			flex: 0 0 auto;
			margin-left: 0;
		}

		.sc-guidance-step-nav-item {
			margin: 0;
		}

		.sc-guidance-step-nav-item + .sc-guidance-step-nav-item {
			margin-top: 8rem;
		}

		.sc-guidance-step-nav-button {
			width: 100%;
			display: flex;
			align-items: flex-start;
			padding: 10rem 12rem;
			border: 1rem solid rgba(150, 172, 238, 0.43);
			border-radius: 8rem;
			background: linear-gradient(165deg, rgba(39, 65, 132, 0.66), rgba(72, 46, 132, 0.58));
			color: #dfeefe;
			cursor: pointer;
			text-align: left;
			box-sizing: border-box;
			overflow: hidden;
		}

		.sc-guidance-step-nav-button.active,
		.sc-guidance-step-nav-button[aria-current="step"] {
			border-color: rgba(121, 232, 255, 0.9);
			background: linear-gradient(165deg, rgba(47, 108, 164, 0.85), rgba(78, 63, 176, 0.78));
			box-shadow: 0 0 0 1rem rgba(93, 176, 255, 0.32);
		}

		.sc-guidance-step-nav-index {
			flex: 0 0 auto;
			min-width: 36rem;
			margin-right: 10rem;
			font-size: 13rem;
			line-height: 1.3;
			font-weight: 700;
			color: rgba(188, 222, 255, 0.96);
		}

		.sc-guidance-step-nav-text {
			display: block;
			flex: 1 1 auto;
			min-width: 0;
			max-width: 100%;
			font-size: 14rem;
			line-height: 1.3;
			color: #ecf5ff;
			white-space: normal !important;
			word-break: break-word;
			overflow-wrap: anywhere;
		}

		.sc-guidance-tutorial-stage {
			flex: 1 1 auto;
			min-width: 0;
		}

		.sc-guidance-step-head {
			display: flex;
			align-items: center;
			flex-wrap: wrap;
			justify-content: space-between;
			gap: 8rem 16rem;
			margin-bottom: 10rem;
		}

		.sc-guidance-step-title {
			margin: 0;
			font-size: 30rem;
			font-weight: 650;
			line-height: 1.2;
			color: #e8f0fb;
		}

		.sc-guidance-step-index {
			font-size: 18rem;
			line-height: 1.2;
			color: rgba(186, 207, 234, 0.95);
			white-space: nowrap;
		}

		.sc-guidance-tutorial-main {
			display: flex;
			align-items: stretch;
		}

		.sc-guidance-tutorial-main.sc-guidance-tutorial-main-no-image {
			display: block;
		}

		.sc-guidance-body {
			display: flex;
			flex-direction: column;
			gap: 8rem;
		}

		.sc-guidance-body-card {
			flex: 1 1 auto;
			border: 1rem solid rgba(166, 188, 246, 0.42);
			border-radius: 12rem;
			background: linear-gradient(165deg, rgba(37, 59, 118, 0.52), rgba(61, 42, 121, 0.44));
			padding: 14rem 16rem;
			min-height: 260rem;
			max-height: 520rem;
			overflow-y: auto;
		}

		.sc-guidance-hero {
			flex: 0 0 52%;
			border: 1rem solid rgba(164, 190, 248, 0.42);
			border-radius: 12rem;
			overflow: hidden;
			background: rgba(14, 22, 58, 0.78);
			display: flex;
			align-items: center;
			justify-content: center;
			min-height: 260rem;
			max-height: 520rem;
			margin-right: 14rem;
			padding: 10rem;
		}

		.sc-guidance-hero-image {
			display: block;
			width: 100%;
			height: 100%;
			object-fit: contain;
			border-radius: 8rem;
			background: rgba(10, 16, 40, 0.62);
		}

		.sc-guidance-hero-fallback {
			width: 100%;
			height: 100%;
			border-radius: 8rem;
			display: flex;
			align-items: center;
			justify-content: center;
			font-size: 16rem;
			line-height: 1.35;
			text-align: center;
			color: rgba(221, 234, 252, 0.95);
			background: linear-gradient(160deg, rgba(35, 62, 126, 0.7), rgba(62, 45, 126, 0.64));
		}

		.sc-guidance-tutorial-main.sc-guidance-tutorial-main-no-image .sc-guidance-body-card {
			max-height: 560rem;
		}

		.sc-guidance-progress-label {
			font-size: 14rem;
			line-height: 1.3;
			color: rgba(199, 218, 241, 0.95);
			display: inline-block;
			white-space: nowrap;
			margin-right: 18rem !important;
		}

		.sc-guidance-paragraph {
			margin: 0;
			font-size: 16rem;
			line-height: 1.46;
			white-space: pre-wrap;
			text-transform: none !important;
			letter-spacing: 0 !important;
		}

		.sc-guidance-list {
			margin: 0;
			padding-left: 22rem;
		}

		.sc-guidance-item {
			margin: 6rem 0;
			font-size: 16rem;
			line-height: 1.44;
			text-transform: none !important;
			letter-spacing: 0 !important;
		}

		.sc-guidance-release-list {
			display: block;
		}

		.sc-guidance-changelog-content {
			display: flex;
			flex-direction: column;
			gap: 14rem;
		}

		.sc-guidance-release {
			border: 1rem solid rgba(160, 194, 232, 0.34);
			border-radius: 12rem;
			overflow: hidden;
			background: rgba(28, 50, 78, 0.62);
		}

		.sc-guidance-release + .sc-guidance-release {
			margin-top: 20rem;
		}

		.sc-guidance-release-toggle {
			width: 100%;
			display: flex;
			align-items: center;
			justify-content: space-between;
			gap: 12rem;
			padding: 14rem 16rem;
			background: rgba(36, 63, 96, 0.72);
			border: 0;
			border-bottom: 1rem solid transparent;
			color: #edf3fb;
			cursor: pointer;
		}

		.sc-guidance-release-toggle[aria-expanded="true"] {
			border-bottom-color: rgba(170, 204, 240, 0.25);
		}

		.sc-guidance-release-left {
			display: flex;
			align-items: center;
			gap: 10rem;
			min-width: 0;
		}

		.sc-guidance-release-version {
			background: rgba(97, 156, 229, 0.34);
			border: 1rem solid rgba(158, 204, 255, 0.65);
			border-radius: 999rem;
			padding: 3rem 10rem;
			font-size: 13rem;
			line-height: 1.2;
			white-space: nowrap;
		}

		.sc-guidance-release-title {
			font-size: 17rem;
			line-height: 1.34;
			font-weight: 600;
			text-align: left;
			text-transform: none !important;
			letter-spacing: 0 !important;
		}

		.sc-guidance-release-toggle-icon {
			width: 24rem;
			height: 24rem;
			border-radius: 999rem;
			display: inline-flex;
			align-items: center;
			justify-content: center;
			font-size: 14rem;
			line-height: 1;
			color: rgba(235, 246, 255, 0.98);
			background: rgba(140, 187, 241, 0.25);
			border: 1rem solid rgba(167, 208, 255, 0.4);
		}

		.sc-guidance-release-body {
			padding: 14rem 16rem 16rem 16rem;
			display: flex;
			flex-direction: column;
			gap: 10rem;
		}

		.sc-guidance-release-body .sc-guidance-paragraph {
			font-size: 17rem;
			line-height: 1.5;
			color: rgba(231, 242, 255, 0.98);
		}

		.sc-guidance-release-body .sc-guidance-list {
			padding-left: 24rem;
		}

		.sc-guidance-release-body .sc-guidance-item {
			margin: 8rem 0;
			font-size: 17rem;
			line-height: 1.5;
		}

		.sc-guidance-footer {
			display: flex;
			align-items: center;
			justify-content: space-between;
			gap: 24rem;
			padding: 18rem 24rem;
			border-top: 1rem solid rgba(174, 191, 247, 0.34);
			background: linear-gradient(160deg, rgba(21, 34, 85, 0.92), rgba(37, 24, 83, 0.92));
		}

		.sc-guidance-footer-left,
		.sc-guidance-footer-mid,
		.sc-guidance-footer-right {
			display: flex;
			align-items: center;
			gap: 16rem;
		}

		.sc-guidance-footer-left {
			flex: 0 0 auto;
			min-width: 260rem;
		}

		.sc-guidance-footer-mid {
			display: flex !important;
			flex-direction: row !important;
			align-items: center !important;
			justify-content: center;
			flex: 1 1 auto;
			gap: 22rem;
			flex-wrap: nowrap;
			white-space: nowrap;
		}

		.sc-guidance-footer-mid > * {
			flex: 0 0 auto;
		}

		.sc-guidance-footer-right {
			gap: 24rem;
		}

		.sc-guidance-footer-right > * + * {
			margin-left: 24rem;
		}

		.sc-guidance-dots {
			display: flex !important;
			flex-direction: row !important;
			flex-wrap: nowrap !important;
			align-items: center !important;
			justify-content: center;
			white-space: nowrap;
			line-height: 0;
		}

		.sc-guidance-dot {
			display: block !important;
			flex: 0 0 auto;
			vertical-align: middle;
		}

		.sc-guidance-dot + .sc-guidance-dot {
			margin-left: 14rem !important;
		}

		.sc-guidance-toggle {
			display: inline-flex;
			align-items: center;
			gap: 10rem;
			padding: 0;
			margin: 0;
			border: 0;
			background: transparent;
			appearance: none;
			-webkit-appearance: none;
			cursor: pointer;
			font-size: 18rem;
			line-height: 1.28;
			color: #e8f0fa;
			text-transform: none !important;
			white-space: nowrap;
		}

		.sc-guidance-toggle:focus-visible {
			outline: 2rem solid rgba(98, 228, 255, 0.68);
			outline-offset: 2rem;
			border-radius: 6rem;
		}

		.sc-guidance-toggle-label {
			display: inline-block;
		}

		.sc-guidance-toggle-check {
			display: inline-flex;
			align-items: center;
			justify-content: center;
			width: 20rem;
			height: 20rem;
			border-radius: 5rem;
			border: 1rem solid rgba(151, 186, 247, 0.78);
			background: rgba(18, 34, 83, 0.88);
			color: rgba(9, 28, 67, 0);
			font-size: 14rem;
			font-weight: 800;
			line-height: 1;
			box-shadow: inset 0 0 0 1rem rgba(10, 18, 47, 0.5);
		}

		.sc-guidance-toggle-check.checked {
			border-color: rgba(123, 238, 255, 0.95);
			background: linear-gradient(180deg, rgba(83, 217, 255, 0.98), rgba(54, 151, 237, 0.98));
			color: rgba(9, 28, 67, 0.98);
		}

		.sc-guidance-dot {
			width: 11rem;
			height: 11rem;
			border-radius: 50%;
			background: rgba(170, 188, 245, 0.54);
		}

		.sc-guidance-dot.active {
			background: #61e5ff;
			box-shadow: 0 0 0 2rem rgba(98, 228, 255, 0.26);
		}

		.sc-guidance-empty {
			font-size: 18rem;
			line-height: 1.3;
			color: rgba(214, 228, 246, 0.88);
		}

		.sc-guidance-btn-secondary,
		.sc-guidance-btn-primary,
		.sc-guidance-btn-secondary button,
		.sc-guidance-btn-primary button {
			min-width: 154rem;
			height: 46rem !important;
			min-height: 46rem !important;
			padding-inline: 24rem;
			border-radius: 10rem !important;
			font-weight: 650;
			line-height: 1 !important;
			letter-spacing: 0 !important;
			text-transform: none !important;
			display: inline-flex !important;
			align-items: center !important;
			justify-content: center !important;
			box-sizing: border-box;
		}

		.sc-guidance-btn-secondary,
		.sc-guidance-btn-secondary button {
			background: linear-gradient(180deg, rgba(88, 125, 229, 0.95), rgba(76, 89, 197, 0.95)) !important;
			border: 1rem solid rgba(178, 196, 255, 0.85) !important;
			color: #f3f8ff !important;
		}

		.sc-guidance-btn-primary,
		.sc-guidance-btn-primary button {
			background: linear-gradient(180deg, rgba(68, 230, 255, 1), rgba(42, 190, 239, 1)) !important;
			border: 1rem solid rgba(142, 244, 255, 0.95) !important;
			color: #062845 !important;
		}

		.sc-guidance-btn-secondary:hover,
		.sc-guidance-btn-secondary button:hover {
			background: linear-gradient(180deg, rgba(104, 142, 240, 0.98), rgba(86, 102, 214, 0.98)) !important;
		}

		.sc-guidance-btn-primary:hover,
		.sc-guidance-btn-primary button:hover {
			background: linear-gradient(180deg, rgba(84, 236, 255, 1), rgba(56, 202, 247, 1)) !important;
		}

		@media (max-width: 1280px) {
			.sc-guidance-panel {
				max-width: 95vw;
			}

			.sc-guidance-header {
				min-height: 96rem;
			}

			.sc-guidance-header-content {
				padding: 14rem 16rem 16rem 16rem;
			}

			.sc-guidance-title {
				font-size: 36rem;
			}

			.sc-guidance-subtitle {
				font-size: 18rem;
				line-height: 1.26;
			}

			.sc-guidance-paragraph,
			.sc-guidance-item {
				font-size: 16rem;
			}

			.sc-guidance-step-title {
				font-size: 24rem;
			}

			.sc-guidance-step-index {
				font-size: 16rem;
			}

			.sc-guidance-toggle {
				font-size: 16rem;
			}

			.sc-guidance-release-toggle {
				padding: 10rem 12rem;
			}

			.sc-guidance-release-title {
				font-size: 15rem;
			}

			.sc-guidance-release + .sc-guidance-release {
				margin-top: 14rem;
			}

			.sc-guidance-release-body {
				padding: 10rem 12rem 12rem 12rem;
			}

			.sc-guidance-release-body .sc-guidance-paragraph,
			.sc-guidance-release-body .sc-guidance-item {
				font-size: 15rem;
				line-height: 1.45;
			}

			.sc-guidance-tutorial-layout {
				display: block;
			}

			.sc-guidance-tutorial-sidebar {
				width: auto;
				height: 220rem;
				max-height: 220rem;
				margin-right: 0;
				margin-bottom: 10rem;
				padding: 10rem;
				overflow: hidden;
				display: block;
			}

			.sc-guidance-tutorial-sidebar-title {
				font-size: 16rem;
			}

			.sc-guidance-tutorial-sidebar-subtitle {
				font-size: 13rem;
				margin-bottom: 8rem;
			}

			.sc-guidance-step-nav-item + .sc-guidance-step-nav-item {
				margin-top: 6rem;
			}

			.sc-guidance-step-nav-button {
				padding: 9rem 10rem;
			}

			.sc-guidance-step-nav-index {
				min-width: 32rem;
				margin-right: 8rem;
				font-size: 12rem;
			}

			.sc-guidance-step-nav-text {
				font-size: 13rem;
			}

			.sc-guidance-step-nav-wrap {
				padding-bottom: 2rem;
			}

			.sc-guidance-step-nav-controls {
				gap: 6rem;
			}

			.sc-guidance-step-nav-controls-top {
				margin-bottom: 8rem;
			}

			.sc-guidance-step-nav-controls-bottom {
				margin-top: 8rem;
				padding: 0 2rem 2rem 2rem;
			}

			.sc-guidance-step-nav-controls-bottom .sc-guidance-step-nav-scroll-button {
				margin-left: 0;
			}

			.sc-guidance-step-nav-scroll-button {
				width: 30rem;
				height: 30rem;
				font-size: 14rem;
			}

			.sc-guidance-step-nav-window-label {
				font-size: 12rem;
			}

			.sc-guidance-tutorial-main {
				display: block;
			}

			.sc-guidance-hero {
				margin-right: 0;
				margin-bottom: 10rem;
				min-height: 180rem;
				max-height: 280rem;
			}

			.sc-guidance-body-card {
				min-height: 180rem;
				max-height: 320rem;
			}

			.sc-guidance-progress-label {
				font-size: 13rem;
				margin-right: 12rem !important;
			}

			.sc-guidance-footer-left {
				min-width: 220rem;
			}

			.sc-guidance-footer-mid {
				display: flex !important;
				flex-direction: row !important;
				align-items: center !important;
				gap: 14rem;
				flex-wrap: nowrap;
				white-space: nowrap;
			}

			.sc-guidance-footer-mid > * {
				flex: 0 0 auto;
			}

			.sc-guidance-dots {
				display: flex !important;
				flex-direction: row !important;
				flex-wrap: nowrap !important;
				white-space: nowrap;
			}

			.sc-guidance-dot + .sc-guidance-dot {
				margin-left: 10rem !important;
			}

			.sc-guidance-footer-right {
				gap: 16rem;
			}

			.sc-guidance-footer-right > * + * {
				margin-left: 16rem;
			}
		}
	`;
}

function safeParseArrayJson(jsonText) {
	if (typeof jsonText !== "string" || jsonText.trim().length === 0) {
		return [];
	}

	try {
		const parsed = JSON.parse(jsonText);
		return Array.isArray(parsed) ? parsed : [];
	}
	catch {
		return [];
	}
}

function normalizeTutorialPages(pagesJson, fallbackBody) {
	const parsed = safeParseArrayJson(pagesJson);
	const normalized = parsed
		.map((entry, idx) => {
			const title = entry && typeof entry.Title === "string"
				? entry.Title.trim()
				: "Step " + String(idx + 1);
			const body = entry && typeof entry.Body === "string"
				? entry.Body.trim()
				: "";
			const imagePath = entry && typeof entry.ImagePath === "string"
				? entry.ImagePath.trim()
				: "";
			const imageCandidates = imagePath.length > 0
				? buildAssetUrlCandidates(imagePath)
				: DEFAULT_TUTORIAL_IMAGE_CANDIDATES;
			return { title, body, imageCandidates };
		})
		.filter((entry) => entry.title.length > 0 || entry.body.length > 0);

	if (normalized.length > 0) {
		return sortTutorialPagesByPreferredOrder(normalized);
	}

	const fallback = String(fallbackBody || "").trim();
	if (fallback.length === 0) {
		return [];
	}

	return [{ title: "Quick Start", body: fallback, imageCandidates: DEFAULT_TUTORIAL_IMAGE_CANDIDATES }];
}

function normalizeChangelogReleases(releasesJson, fallbackVersion, fallbackBody) {
	const parsed = safeParseArrayJson(releasesJson);
	const normalized = parsed
		.map((entry, idx) => {
			const version = entry && typeof entry.Version === "string"
				? entry.Version.trim()
				: idx === 0
					? String(fallbackVersion || "").trim()
					: "";
			const title = entry && typeof entry.Title === "string"
				? entry.Title.trim()
				: "Release notes";
			const body = entry && typeof entry.Body === "string"
				? entry.Body.trim()
				: "";
			return { version, title, body };
		})
		.filter((entry) => entry.title.length > 0 || entry.body.length > 0);

	if (normalized.length > 0) {
		return normalized;
	}

	const fallback = String(fallbackBody || "").trim();
	if (fallback.length === 0) {
		return [];
	}

	return [{ version: String(fallbackVersion || "").trim(), title: "Release notes", body: fallback }];
}

function renderBodyNodes(text) {
	const lines = String(text || "").split(/\r?\n/);
	const nodes = [];
	let bullets = [];
	let keyIndex = 0;

	const flushBullets = () => {
		if (bullets.length === 0) {
			return;
		}

		nodes.push(
			React.createElement(
				"ul",
				{ key: "ul-" + keyIndex++, className: "sc-guidance-list" },
				bullets.map((item, idx) => React.createElement("li", { key: "li-" + keyIndex + "-" + idx, className: "sc-guidance-item" }, item))
			)
		);
		bullets = [];
	};

	for (const rawLine of lines) {
		const trimmed = String(rawLine || "").trim();
		if (trimmed.length === 0) {
			flushBullets();
			continue;
		}

		if (trimmed.startsWith("- ")) {
			bullets.push(trimmed.slice(2));
			continue;
		}

		flushBullets();
		nodes.push(React.createElement("p", { key: "p-" + keyIndex++, className: "sc-guidance-paragraph" }, trimmed));
	}

	flushBullets();

	if (nodes.length === 0) {
		return [React.createElement("div", { key: "empty", className: "sc-guidance-empty" }, "No guidance content is available.")];
	}

	return nodes;
}

function GuidanceShell(props) {
	const { title, subtitle, onClose, content, footer, bannerImageCandidates } = props;

	return React.createElement(
		"div",
		{ className: "sc-guidance-overlay" },
		React.createElement(
			ui.Panel,
			{ className: "sc-guidance-panel" },
			React.createElement(
				"div",
				{ className: "sc-guidance-shell" },
				React.createElement(
					"div",
					{ className: "sc-guidance-header" },
					React.createElement(
						"div",
						{ className: "sc-guidance-header-banner" },
						React.createElement(FallbackImage, {
							className: "sc-guidance-header-banner-image",
							srcCandidates: bannerImageCandidates,
							alt: "Guidance banner"
						}),
						React.createElement("div", { className: "sc-guidance-header-banner-overlay" })
					),
						React.createElement(
							"div",
							{ className: "sc-guidance-header-content" },
							React.createElement(
								"div",
								{ className: "sc-guidance-header-main" },
								React.createElement(
									"div",
									{ className: "sc-guidance-title-wrap" },
									React.createElement("h2", { className: "sc-guidance-title" }, title),
								subtitle ? React.createElement("p", { className: "sc-guidance-subtitle" }, subtitle) : null
							)
						),
						React.createElement(ui.Button, { className: "sc-guidance-close", onSelect: onClose, "aria-label": "Close" }, "×")
					),
				),
				React.createElement("div", { className: "sc-guidance-content" }, content),
				React.createElement("div", { className: "sc-guidance-footer" }, footer)
			)
		)
	);
}

function GuidanceRoot() {
	React.useEffect(() => {
		ensureStyles();
	}, []);

	const tutorialVisible = api.useValue(tutorialVisibleBinding);
	const changelogVisible = api.useValue(changelogVisibleBinding);
	const tutorialTitle = api.useValue(tutorialTitleBinding);
	const tutorialBody = api.useValue(tutorialBodyBinding);
	const tutorialPagesJson = api.useValue(tutorialPagesBinding);
	const changelogTitle = api.useValue(changelogTitleBinding);
	const changelogVersion = api.useValue(changelogVersionBinding);
	const changelogBody = api.useValue(changelogBodyBinding);
	const changelogReleasesJson = api.useValue(changelogReleasesBinding);

	const tutorialPages = React.useMemo(
		() => normalizeTutorialPages(tutorialPagesJson, tutorialBody),
		[tutorialPagesJson, tutorialBody]
	);
	const changelogReleases = React.useMemo(
		() => normalizeChangelogReleases(changelogReleasesJson, changelogVersion, changelogBody),
		[changelogReleasesJson, changelogVersion, changelogBody]
	);

	React.useEffect(() => {
		if (!tutorialVisible) {
			return;
		}

		const preloadSources = [];
		if (Array.isArray(TUTORIAL_HEADER_BANNER_IMAGE_CANDIDATES) && TUTORIAL_HEADER_BANNER_IMAGE_CANDIDATES.length > 0) {
			preloadSources.push(TUTORIAL_HEADER_BANNER_IMAGE_CANDIDATES[0]);
		}

		for (const page of tutorialPages) {
			const pageCandidates = Array.isArray(page && page.imageCandidates) && page.imageCandidates.length > 0
				? page.imageCandidates
				: DEFAULT_TUTORIAL_IMAGE_CANDIDATES;
			if (Array.isArray(pageCandidates) && pageCandidates.length > 0) {
				preloadSources.push(pageCandidates[0]);
			}
		}

		preloadImageSourcesIfNeeded(preloadSources);
	}, [tutorialVisible, tutorialPages]);

	React.useEffect(() => {
		if (!changelogVisible) {
			return;
		}

		if (Array.isArray(CHANGELOG_HEADER_BANNER_IMAGE_CANDIDATES) && CHANGELOG_HEADER_BANNER_IMAGE_CANDIDATES.length > 0) {
			preloadImageSourcesIfNeeded([CHANGELOG_HEADER_BANNER_IMAGE_CANDIDATES[0]]);
		}
	}, [changelogVisible]);

	const [dontShowAgain, setDontShowAgain] = React.useState(false);
	const [tutorialPageIndex, setTutorialPageIndex] = React.useState(0);
	const [tutorialNavStartIndex, setTutorialNavStartIndex] = React.useState(0);
	const [expandedReleases, setExpandedReleases] = React.useState({});

	React.useEffect(() => {
		if (tutorialVisible) {
			setTutorialPageIndex(0);
			setTutorialNavStartIndex(0);
		}
		else {
			setDontShowAgain(false);
		}
	}, [tutorialVisible]);

	React.useEffect(() => {
		if (!tutorialVisible) {
			return;
		}

		const totalPages = Math.max(1, tutorialPages.length);
		const navWindowSize = getTutorialNavWindowSize();
		const maxStart = Math.max(0, totalPages - navWindowSize);
		const clampedPage = Math.min(Math.max(0, tutorialPageIndex), totalPages - 1);
		setTutorialNavStartIndex((current) => {
			let next = current;
			if (clampedPage < next) {
				next = clampedPage;
			}
			else if (clampedPage >= next + navWindowSize) {
				next = clampedPage - navWindowSize + 1;
			}

			if (next < 0) {
				next = 0;
			}
			if (next > maxStart) {
				next = maxStart;
			}
			return next;
		});
	}, [tutorialVisible, tutorialPageIndex, tutorialPages.length]);

	React.useEffect(() => {
		if (!changelogVisible) {
			return;
		}

		const initial = {};
		for (let i = 0; i < changelogReleases.length; i++) {
			initial[i] = i === 0;
		}
		setExpandedReleases(initial);
	}, [changelogVisible, changelogReleases]);

	if (!tutorialVisible && !changelogVisible) {
		return null;
	}

	const closeTutorial = () => {
		const suppressTutorial = Boolean(dontShowAgain);
		api.trigger(GROUP, suppressTutorial ? "closeTutorialDontShowAgain" : "closeTutorial");
	};

	const closeChangelog = () => {
		api.trigger(GROUP, "closeChangelog");
	};

	if (tutorialVisible) {
		const totalPages = Math.max(1, tutorialPages.length);
		const clampedIndex = Math.min(Math.max(0, tutorialPageIndex), totalPages - 1);
		const navWindowSize = getTutorialNavWindowSize();
		const maxNavStart = Math.max(0, totalPages - navWindowSize);
		const navStart = Math.min(Math.max(0, tutorialNavStartIndex), maxNavStart);
		const navEndExclusive = Math.min(totalPages, navStart + navWindowSize);
		const currentPage = tutorialPages[clampedIndex] || { title: "Quick Start", body: tutorialBody };
		const tutorialImageCandidates = Array.isArray(currentPage.imageCandidates) && currentPage.imageCandidates.length > 0
			? currentPage.imageCandidates
			: DEFAULT_TUTORIAL_IMAGE_CANDIDATES;
		const onBack = () => setTutorialPageIndex((index) => Math.max(0, index - 1));
		const onNext = () => {
			if (clampedIndex >= totalPages - 1) {
				closeTutorial();
				return;
			}
			setTutorialPageIndex((index) => Math.min(totalPages - 1, index + 1));
		};
		const canNavUp = navStart > 0;
		const canNavDown = navEndExclusive < totalPages;
		const onNavUp = () => setTutorialNavStartIndex((index) => Math.max(0, index - 1));
		const onNavDown = () => setTutorialNavStartIndex((index) => Math.min(maxNavStart, index + 1));

		const stepNavigation = [];
		for (let i = navStart; i < navEndExclusive; i++) {
			const pageTitle = tutorialPages[i] && tutorialPages[i].title
				? tutorialPages[i].title
				: "Step " + String(i + 1);
			stepNavigation.push(
				React.createElement(
					"li",
					{ key: "step-nav-item-" + i, className: "sc-guidance-step-nav-item" },
					React.createElement(
						"button",
						{
							type: "button",
							className: "sc-guidance-step-nav-button" + (i === clampedIndex ? " active" : ""),
							onClick: () => setTutorialPageIndex(i),
							"aria-current": i === clampedIndex ? "step" : undefined
						},
						React.createElement("span", { className: "sc-guidance-step-nav-index" }, String(i + 1).padStart(2, "0")),
						React.createElement("span", { className: "sc-guidance-step-nav-text" }, pageTitle)
					)
				)
			);
		}

		const content = React.createElement(
			"div",
			{ className: "sc-guidance-tutorial-layout" },
			React.createElement(
				"div",
				{ className: "sc-guidance-tutorial-sidebar" },
				React.createElement("h4", { className: "sc-guidance-tutorial-sidebar-title" }, "Quick Start Topics"),
				React.createElement("p", { className: "sc-guidance-tutorial-sidebar-subtitle" }, "Jump directly to any section."),
				React.createElement(
					"div",
					{ className: "sc-guidance-step-nav-controls sc-guidance-step-nav-controls-top" },
					React.createElement(
						"button",
						{
							type: "button",
							className: "sc-guidance-step-nav-scroll-button",
							onClick: onNavUp,
							disabled: !canNavUp,
							"aria-label": "Show previous topics"
						},
						"▲"
					)
				),
				React.createElement(
					"div",
					{ className: "sc-guidance-step-nav-wrap" },
					React.createElement("ol", { className: "sc-guidance-step-nav" }, stepNavigation)
				),
				React.createElement(
					"div",
					{ className: "sc-guidance-step-nav-controls sc-guidance-step-nav-controls-bottom" },
					React.createElement(
						"span",
						{ className: "sc-guidance-step-nav-window-label" },
						String(navStart + 1) + "-" + String(navEndExclusive) + " of " + String(totalPages)
					),
					React.createElement(
						"button",
						{
							type: "button",
							className: "sc-guidance-step-nav-scroll-button",
							onClick: onNavDown,
							disabled: !canNavDown,
							"aria-label": "Show more topics"
						},
						"▼"
					)
				)
			),
			React.createElement(
				"div",
				{ className: "sc-guidance-tutorial-stage" },
				React.createElement(
					"div",
					{ className: "sc-guidance-step-head" },
					React.createElement("h3", { className: "sc-guidance-step-title" }, currentPage.title || "Quick Start"),
					React.createElement("span", { className: "sc-guidance-step-index" }, "Step " + String(clampedIndex + 1) + " of " + String(totalPages))
				),
				React.createElement(
					"div",
					{
						className: "sc-guidance-tutorial-main" + (tutorialImageCandidates.length > 0 ? "" : " sc-guidance-tutorial-main-no-image")
					},
					tutorialImageCandidates.length > 0
						? React.createElement(
							"div",
							{ className: "sc-guidance-hero" },
							React.createElement(FallbackImage, {
								className: "sc-guidance-hero-image",
								srcCandidates: tutorialImageCandidates,
								alt: "Tutorial preview",
								fallback: React.createElement("div", { className: "sc-guidance-hero-fallback" }, "Tutorial preview unavailable")
							})
						)
						: null,
					React.createElement(
						"div",
						{ className: "sc-guidance-body-card" },
						React.createElement("div", { className: "sc-guidance-body" }, renderBodyNodes(currentPage.body))
					)
				)
			)
		);

		const dots = [];
		for (let i = 0; i < totalPages; i++) {
			dots.push(
				React.createElement("div", { key: "dot-" + i, className: "sc-guidance-dot" + (i === clampedIndex ? " active" : "") })
			);
		}

		const footer = React.createElement(
			React.Fragment,
			null,
			React.createElement(
				"div",
				{ className: "sc-guidance-footer-left" },
				React.createElement(
					"button",
					{
						type: "button",
						className: "sc-guidance-toggle",
						role: "checkbox",
						"aria-checked": dontShowAgain ? "true" : "false",
						onClick: () => setDontShowAgain((current) => !current)
					},
					React.createElement("span", { className: "sc-guidance-toggle-label" }, "Don't show again"),
					React.createElement("span", { className: "sc-guidance-toggle-check" + (dontShowAgain ? " checked" : "") }, dontShowAgain ? "✓" : "")
				)
				),
			React.createElement(
				"div",
				{ className: "sc-guidance-footer-mid" },
				React.createElement("span", { className: "sc-guidance-progress-label" }, "Step " + String(clampedIndex + 1) + " of " + String(totalPages)),
				React.createElement("div", { className: "sc-guidance-dots" }, dots)
			),
			React.createElement(
				"div",
				{ className: "sc-guidance-footer-right" },
				React.createElement(ui.Button, { className: "sc-guidance-btn-secondary", onSelect: onBack, disabled: clampedIndex <= 0 }, "Back"),
				React.createElement(ui.Button, { className: "sc-guidance-btn-primary", variant: "primary", onSelect: onNext }, clampedIndex >= totalPages - 1 ? "Finish" : "Next")
			)
		);

		return React.createElement(
			GuidanceOverlayPortal,
			null,
			React.createElement(GuidanceShell, {
				title: tutorialTitle,
				subtitle: "Shown once on first load, then available from General > Help & Guidance.",
					onClose: closeTutorial,
					content,
					footer,
					bannerImageCandidates: TUTORIAL_HEADER_BANNER_IMAGE_CANDIDATES
				})
			);
		}

	const firstVersion = changelogReleases.length > 0 && changelogReleases[0].version
		? changelogReleases[0].version
		: String(changelogVersion || "").trim();
	const subtitle = firstVersion ? "Latest release " + firstVersion : "Release notes";

	const toggleRelease = (index) => {
		setExpandedReleases((current) => {
			const next = Object.assign({}, current);
			next[index] = !Boolean(current[index]);
			return next;
		});
	};

	const releasesList = changelogReleases.length > 0
		? React.createElement(
			"div",
			{ className: "sc-guidance-release-list" },
			changelogReleases.map((entry, index) => {
				const isExpanded = Boolean(expandedReleases[index]);
				const releaseBodyId = "sc-guidance-release-body-" + String(index);
				return React.createElement(
					"div",
					{ key: "release-" + index, className: "sc-guidance-release" },
					React.createElement(
						"button",
						{
							type: "button",
							className: "sc-guidance-release-toggle",
							onClick: () => toggleRelease(index),
							"aria-expanded": isExpanded,
							"aria-controls": releaseBodyId
						},
						React.createElement(
							"span",
							{ className: "sc-guidance-release-left" },
							entry.version
								? React.createElement("span", { className: "sc-guidance-release-version" }, entry.version)
								: null,
							React.createElement("span", { className: "sc-guidance-release-title" }, entry.title || "Release")
						),
						React.createElement("span", { className: "sc-guidance-release-toggle-icon" }, isExpanded ? "-" : "+")
					),
					isExpanded
						? React.createElement("div", { id: releaseBodyId, className: "sc-guidance-release-body" }, renderBodyNodes(entry.body))
						: null
				);
			})
		)
		: React.createElement("div", { className: "sc-guidance-empty" }, "No changelog entries are available.");

	const releasesContent = React.createElement(
		"div",
		{ className: "sc-guidance-changelog-content" },
		releasesList
	);

	const changelogFooter = React.createElement(
		React.Fragment,
		null,
		React.createElement("div", { className: "sc-guidance-footer-left" }),
		React.createElement("div", { className: "sc-guidance-footer-mid" }),
		React.createElement(
			"div",
			{ className: "sc-guidance-footer-right" },
			React.createElement(ui.Button, { className: "sc-guidance-btn-primary", variant: "primary", onSelect: closeChangelog }, "Close")
		)
	);

	return React.createElement(
		GuidanceOverlayPortal,
		null,
		React.createElement(GuidanceShell, {
			title: changelogTitle,
			subtitle,
				onClose: closeChangelog,
				content: releasesContent,
				footer: changelogFooter,
				bannerImageCandidates: CHANGELOG_HEADER_BANNER_IMAGE_CANDIDATES
			})
		);
	}

const register = (moduleRegistry) => {
	moduleRegistry.append("Game", GuidanceRoot);
};

const hasCSS = false;

export { hasCSS, register as default };
