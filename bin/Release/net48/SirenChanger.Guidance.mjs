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

const MODULE_BASE_URL = (() => {
	try {
		return new URL(".", import.meta.url).href;
	}
	catch {
		return "";
	}
})();

function getAssetUrl(relativePath) {
	const cleaned = String(relativePath || "").replace(/\\/g, "/").replace(/^\/+/, "");
	if (!cleaned) {
		return "";
	}

	try {
		const base = MODULE_BASE_URL || (typeof window !== "undefined" && window.location ? window.location.href : "");
		return new URL(cleaned, base).href;
	}
	catch {
		return cleaned;
	}
}

const LOGO_IMAGE_URL = getAssetUrl("Images/Audio Switcher Logo.jpg");
const TUTORIAL_HEADER_BANNER_URL = getAssetUrl("Images/GuidanceBanner_Tutorial.svg");
const CHANGELOG_HEADER_BANNER_URL = getAssetUrl("Images/GuidanceBanner_Changelog.svg");
const CHANGELOG_HERO_IMAGE_URL = getAssetUrl("Images/Screen_Dev2.jpg");
const TUTORIAL_PAGE_IMAGE_URLS = [
	getAssetUrl("Images/Screen_Gen.jpg"),
	getAssetUrl("Images/Screen_Siren.jpg"),
	getAssetUrl("Images/Screen_Engine.jpg"),
	getAssetUrl("Images/Screen_Ambient.jpg"),
	getAssetUrl("Images/Screen_PT.jpg"),
	getAssetUrl("Images/Screen_Prof.jpg"),
	getAssetUrl("Images/Screen_Siren2.jpg"),
	getAssetUrl("Images/Screen_Gen.jpg"),
	getAssetUrl("Images/Screen_Dev.jpg"),
	getAssetUrl("Images/Screen_Dev2.jpg"),
	getAssetUrl("Images/Screen_Dev3.jpg")
];

function getTutorialPageImageUrl(pageIndex) {
	if (!Number.isFinite(pageIndex)) {
		return TUTORIAL_PAGE_IMAGE_URLS[0];
	}

	const clamped = Math.max(0, Math.min(TUTORIAL_PAGE_IMAGE_URLS.length - 1, Math.floor(pageIndex)));
	return TUTORIAL_PAGE_IMAGE_URLS[clamped] || TUTORIAL_PAGE_IMAGE_URLS[0];
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
			width: 980rem;
			max-width: 88vw;
			max-height: 82vh;
			border-radius: 16rem;
			overflow: hidden;
			--panelRadius: 16rem;
			animation: 220ms sc-guidance-fade-in ease-out forwards;
		}

		.sc-guidance-shell {
			display: flex;
			flex-direction: column;
			background: linear-gradient(180deg, rgba(20, 32, 47, 0.98), rgba(17, 27, 39, 0.98));
			max-height: 82vh;
		}

		.sc-guidance-header {
			display: flex;
			flex-direction: column;
			padding: 0;
			border-bottom: 1rem solid rgba(255, 255, 255, 0.14);
			background: linear-gradient(160deg, rgba(20, 35, 53, 0.98), rgba(17, 30, 47, 0.98));
		}

		.sc-guidance-header-banner {
			height: 106rem;
			background: linear-gradient(135deg, rgba(38, 73, 110, 0.92), rgba(22, 42, 64, 0.95));
			background-size: cover;
			background-position: center;
			background-repeat: no-repeat;
		}

		.sc-guidance-header-content {
			display: flex;
			align-items: center;
			justify-content: space-between;
			gap: 10rem;
			padding: 9rem 16rem 10rem 16rem;
			background: linear-gradient(180deg, rgba(14, 26, 40, 0.80), rgba(14, 26, 40, 0.98));
		}

		.sc-guidance-header-main {
			display: flex;
			align-items: center;
			gap: 10rem;
			min-width: 0;
		}

		.sc-guidance-logo {
			width: 52rem;
			height: 52rem;
			border-radius: 8rem;
			object-fit: cover;
			border: 1rem solid rgba(215, 239, 255, 0.45);
			background: rgba(8, 16, 26, 0.66);
			box-shadow: 0 4rem 12rem rgba(0, 0, 0, 0.25);
		}

		.sc-guidance-title-wrap {
			display: flex;
			flex-direction: column;
			gap: 1rem;
			min-width: 0;
		}

		.sc-guidance-title {
			margin: 0;
			font-size: 35rem;
			line-height: 1.06;
			font-weight: 700;
			color: #f1f6ff;
			text-transform: none !important;
			letter-spacing: 0 !important;
		}

		.sc-guidance-subtitle {
			margin: 0;
			font-size: 17rem;
			line-height: 1.16;
			color: rgba(227, 236, 248, 0.96);
			text-transform: none !important;
			letter-spacing: 0 !important;
		}

		.sc-guidance-close {
			flex: 0 0 auto;
			width: 30rem;
			height: 30rem;
			border-radius: 6rem;
			border: 1rem solid rgba(255, 255, 255, 0.28);
			background: rgba(8, 14, 24, 0.48);
			color: #ecf3fd;
			font-size: 19rem;
			line-height: 1;
			font-weight: 700;
			padding: 0;
			display: inline-flex;
			align-items: center;
			justify-content: center;
		}

		.sc-guidance-content {
			padding: 12rem 14rem 11rem 14rem;
			overflow-y: auto;
			max-height: min(56vh, 620rem);
			display: flex;
			flex-direction: column;
			gap: 9rem;
			color: #edf3fb;
		}

		.sc-guidance-step-head {
			display: flex;
			align-items: baseline;
			justify-content: space-between;
			gap: 8rem;
		}

		.sc-guidance-step-title {
			margin: 0;
			font-size: 25rem;
			font-weight: 650;
			line-height: 1.14;
			color: #e8f0fb;
		}

		.sc-guidance-step-index {
			font-size: 16rem;
			line-height: 1.15;
			color: rgba(186, 207, 234, 0.95);
			white-space: nowrap;
		}

		.sc-guidance-body {
			display: flex;
			flex-direction: column;
			gap: 5rem;
		}

		.sc-guidance-hero {
			border: 1rem solid rgba(171, 211, 255, 0.25);
			border-radius: 10rem;
			overflow: hidden;
			background: rgba(12, 24, 36, 0.75);
			display: flex;
			align-items: center;
			justify-content: center;
			padding: 4rem;
		}

		.sc-guidance-hero-image {
			display: block;
			width: 100%;
			height: auto;
			max-height: min(34vh, 340rem);
			object-fit: contain;
			object-position: center;
		}

		.sc-guidance-paragraph {
			margin: 0;
			font-size: 22rem;
			line-height: 1.2;
			white-space: pre-wrap;
			text-transform: none !important;
			letter-spacing: 0 !important;
		}

		.sc-guidance-list {
			margin: 0;
			padding-left: 16rem;
		}

		.sc-guidance-item {
			margin: 1rem 0;
			font-size: 22rem;
			line-height: 1.2;
			text-transform: none !important;
			letter-spacing: 0 !important;
		}

		.sc-guidance-release-list {
			display: flex;
			flex-direction: column;
			gap: 7rem;
		}

		.sc-guidance-release {
			border: 1rem solid rgba(255, 255, 255, 0.12);
			border-radius: 10rem;
			overflow: hidden;
			background: rgba(15, 25, 37, 0.6);
		}

		.sc-guidance-release-toggle {
			width: 100%;
			display: flex;
			align-items: center;
			justify-content: space-between;
			gap: 10rem;
			padding: 8rem 10rem;
			background: rgba(24, 38, 56, 0.75);
			border: 0;
			color: #edf3fb;
		}

		.sc-guidance-release-left {
			display: flex;
			align-items: center;
			gap: 8rem;
			min-width: 0;
		}

		.sc-guidance-release-version {
			background: rgba(73, 134, 208, 0.3);
			border: 1rem solid rgba(122, 177, 242, 0.5);
			border-radius: 999rem;
			padding: 2rem 7rem;
			font-size: 14rem;
			line-height: 1.12;
			white-space: nowrap;
		}

		.sc-guidance-release-title {
			font-size: 18rem;
			line-height: 1.16;
			font-weight: 600;
			text-align: left;
			text-transform: none !important;
			letter-spacing: 0 !important;
		}

		.sc-guidance-release-toggle-icon {
			font-size: 16rem;
			line-height: 1;
			color: rgba(221, 236, 255, 0.9);
		}

		.sc-guidance-release-body {
			padding: 8rem 10rem 10rem 10rem;
			display: flex;
			flex-direction: column;
			gap: 4rem;
		}

		.sc-guidance-footer {
			display: flex;
			align-items: center;
			justify-content: space-between;
			gap: 10rem;
			padding: 8rem 14rem 9rem 14rem;
			border-top: 1rem solid rgba(255, 255, 255, 0.14);
			background: rgba(9, 16, 25, 0.92);
		}

		.sc-guidance-footer-left,
		.sc-guidance-footer-mid,
		.sc-guidance-footer-right {
			display: flex;
			align-items: center;
			gap: 6rem;
		}

		.sc-guidance-footer-mid {
			justify-content: center;
			flex: 1 1 auto;
		}

		.sc-guidance-toggle {
			display: inline-flex;
			align-items: center;
			gap: 6rem;
			font-size: 19rem;
			line-height: 1.12;
			color: #e8f0fa;
			text-transform: none !important;
		}

		.sc-guidance-toggle input {
			width: 14rem;
			height: 14rem;
		}

		.sc-guidance-dot {
			width: 7rem;
			height: 7rem;
			border-radius: 50%;
			background: rgba(179, 205, 234, 0.35);
		}

		.sc-guidance-dot.active {
			background: #4cb4ff;
		}

		.sc-guidance-empty {
			font-size: 19rem;
			line-height: 1.2;
			color: rgba(214, 228, 246, 0.88);
		}

		@media (max-width: 1280px) {
			.sc-guidance-panel {
				max-width: 92vw;
			}

			.sc-guidance-header-banner {
				height: 88rem;
			}

			.sc-guidance-title {
				font-size: 30rem;
			}

			.sc-guidance-subtitle {
				font-size: 15rem;
			}

			.sc-guidance-paragraph,
			.sc-guidance-item {
				font-size: 20rem;
			}

			.sc-guidance-step-title {
				font-size: 21rem;
			}

			.sc-guidance-toggle {
				font-size: 17rem;
			}

			.sc-guidance-logo {
				width: 44rem;
				height: 44rem;
			}

			.sc-guidance-hero-image {
				max-height: min(28vh, 250rem);
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
			return { title, body };
		})
		.filter((entry) => entry.title.length > 0 || entry.body.length > 0);

	if (normalized.length > 0) {
		return normalized;
	}

	const fallback = String(fallbackBody || "").trim();
	if (fallback.length === 0) {
		return [];
	}

	return [{ title: "Quick Start", body: fallback }];
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
	const { title, subtitle, onClose, content, footer, bannerImageUrl, logoImageUrl, showLogo } = props;
	const headerBannerStyle = bannerImageUrl
		? { backgroundImage: "linear-gradient(145deg, rgba(13, 28, 44, 0.40), rgba(13, 28, 44, 0.60)), url(\"" + bannerImageUrl + "\")" }
		: undefined;

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
					React.createElement("div", { className: "sc-guidance-header-banner", style: headerBannerStyle }),
					React.createElement(
						"div",
						{ className: "sc-guidance-header-content" },
						React.createElement(
							"div",
							{ className: "sc-guidance-header-main" },
							showLogo && logoImageUrl
								? React.createElement("img", {
									className: "sc-guidance-logo",
									src: logoImageUrl,
									alt: "Audio Switcher logo"
								})
								: null,
							React.createElement(
								"div",
								{ className: "sc-guidance-title-wrap" },
								React.createElement("h2", { className: "sc-guidance-title" }, title),
								subtitle ? React.createElement("p", { className: "sc-guidance-subtitle" }, subtitle) : null
							)
						),
						React.createElement("button", { type: "button", className: "sc-guidance-close", onClick: onClose, "aria-label": "Close" }, "x")
					),
				),
				React.createElement("div", { className: "sc-guidance-content" }, content),
				React.createElement("div", { className: "sc-guidance-footer" }, footer)
			)
		)
	);
}

function GuidanceRoot() {
	ensureStyles();

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

	const [dontShowAgain, setDontShowAgain] = React.useState(false);
	const [tutorialPageIndex, setTutorialPageIndex] = React.useState(0);
	const [expandedReleases, setExpandedReleases] = React.useState({});

	React.useEffect(() => {
		if (tutorialVisible) {
			setTutorialPageIndex(0);
		}
		else {
			setDontShowAgain(false);
		}
	}, [tutorialVisible]);

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
		api.trigger(GROUP, "closeTutorial", Boolean(dontShowAgain));
	};

	const closeChangelog = () => {
		api.trigger(GROUP, "closeChangelog");
	};

	if (tutorialVisible) {
		const totalPages = Math.max(1, tutorialPages.length);
		const clampedIndex = Math.min(Math.max(0, tutorialPageIndex), totalPages - 1);
		const currentPage = tutorialPages[clampedIndex] || { title: "Quick Start", body: tutorialBody };
		const tutorialImageUrl = getTutorialPageImageUrl(clampedIndex);
		const onBack = () => setTutorialPageIndex((index) => Math.max(0, index - 1));
		const onNext = () => {
			if (clampedIndex >= totalPages - 1) {
				closeTutorial();
				return;
			}
			setTutorialPageIndex((index) => Math.min(totalPages - 1, index + 1));
		};

		const content = React.createElement(
			React.Fragment,
			null,
			tutorialImageUrl
				? React.createElement(
					"div",
					{ className: "sc-guidance-hero" },
					React.createElement("img", { className: "sc-guidance-hero-image", src: tutorialImageUrl, alt: "Tutorial preview" })
				)
				: null,
			React.createElement(
				"div",
				{ className: "sc-guidance-step-head" },
				React.createElement("h3", { className: "sc-guidance-step-title" }, currentPage.title || "Quick Start"),
				React.createElement("span", { className: "sc-guidance-step-index" }, "Step " + String(clampedIndex + 1) + " of " + String(totalPages))
			),
			React.createElement("div", { className: "sc-guidance-body" }, renderBodyNodes(currentPage.body))
		);

		const dots = [];
		for (let i = 0; i < totalPages; i++) {
			dots.push(
				React.createElement("span", { key: "dot-" + i, className: "sc-guidance-dot" + (i === clampedIndex ? " active" : "") })
			);
		}

		const footer = React.createElement(
			React.Fragment,
			null,
			React.createElement(
				"div",
				{ className: "sc-guidance-footer-left" },
				React.createElement(
					"label",
					{ className: "sc-guidance-toggle" },
					React.createElement("input", {
						type: "checkbox",
						checked: dontShowAgain,
						onChange: (event) => setDontShowAgain(Boolean(event && event.target && event.target.checked))
					}),
					"Don't show again"
				)
			),
			React.createElement("div", { className: "sc-guidance-footer-mid" }, dots),
			React.createElement(
				"div",
				{ className: "sc-guidance-footer-right" },
				React.createElement(ui.Button, { onSelect: onBack, disabled: clampedIndex <= 0 }, "Back"),
				React.createElement(ui.Button, { variant: "primary", onSelect: onNext }, clampedIndex >= totalPages - 1 ? "Finish" : "Next")
			)
		);

		return React.createElement(
			GuidanceOverlayPortal,
			null,
			React.createElement(GuidanceShell, {
				title: tutorialTitle,
				subtitle: "Guided setup",
				onClose: closeTutorial,
				content,
				footer,
				bannerImageUrl: TUTORIAL_HEADER_BANNER_URL,
				logoImageUrl: LOGO_IMAGE_URL,
				showLogo: clampedIndex === 0
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
				return React.createElement(
					"div",
					{ key: "release-" + index, className: "sc-guidance-release" },
					React.createElement(
						"button",
						{
							type: "button",
							className: "sc-guidance-release-toggle",
							onClick: () => toggleRelease(index)
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
						? React.createElement("div", { className: "sc-guidance-release-body" }, renderBodyNodes(entry.body))
						: null
				);
			})
		)
		: React.createElement("div", { className: "sc-guidance-empty" }, "No changelog entries are available.");

	const releasesContent = React.createElement(
		React.Fragment,
		null,
		CHANGELOG_HERO_IMAGE_URL
			? React.createElement(
				"div",
				{ className: "sc-guidance-hero" },
				React.createElement("img", { className: "sc-guidance-hero-image", src: CHANGELOG_HERO_IMAGE_URL, alt: "Changelog preview" })
			)
			: null,
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
			React.createElement(ui.Button, { variant: "primary", onSelect: closeChangelog }, "Close")
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
			bannerImageUrl: CHANGELOG_HEADER_BANNER_URL,
			logoImageUrl: LOGO_IMAGE_URL,
			showLogo: true
		})
	);
}

const register = (moduleRegistry) => {
	moduleRegistry.append("Game", GuidanceRoot);
};

const hasCSS = false;

export { hasCSS, register as default };
