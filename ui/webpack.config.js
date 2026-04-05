const os = require("os");
const path = require("path");
const CopyWebpackPlugin = require("copy-webpack-plugin");
const MOD = require("./mod.json");

const CSII_USERDATAPATH = process.env.CSII_USERDATAPATH || path.join(
	os.homedir(),
	"AppData",
	"LocalLow",
	"Colossal Order",
	"Cities Skylines II"
);

const OUTPUT_DIR = path.join(CSII_USERDATAPATH, "Mods", MOD.modRootId);

const EXTERNALS = {
	react: "React",
	"react-dom": "ReactDOM",
	"cs2/modding": "cs2/modding",
	"cs2/api": "cs2/api",
	"cs2/bindings": "cs2/bindings",
	"cs2/l10n": "cs2/l10n",
	"cs2/ui": "cs2/ui",
	"cs2/input": "cs2/input",
	"cs2/utils": "cs2/utils",
	"cohtml/cohtml": "cohtml/cohtml"
};

module.exports = (_, argv) => {
	const mode = argv && argv.mode === "production" ? "production" : "development";
	const isDevelopment = mode !== "production";

	return {
		mode,
		stats: "minimal",
		devtool: isDevelopment ? "eval-cheap-module-source-map" : false,
		entry: {
			[MOD.id]: path.resolve(__dirname, "src", "index.js")
		},
		module: {
			rules: [
				{
					test: /\.m?js$/,
					parser: {
						javascript: {
							url: false
						}
					}
				}
			]
		},
		externalsType: "window",
		externals: EXTERNALS,
		output: {
			path: OUTPUT_DIR,
			filename: "[name].mjs",
			library: { type: "module" },
			publicPath: "coui://ui-mods/",
			clean: false
		},
		performance: {
			hints: false
		},
		experiments: {
			outputModule: true
		},
		plugins: [
			new CopyWebpackPlugin({
				patterns: [
					{
						from: path.resolve(__dirname, "..", "Images"),
						to: path.resolve(OUTPUT_DIR, "Images"),
						noErrorOnMissing: true
					}
				]
			})
		],
		watchOptions: {
			ignored: /node_modules/
		}
	};
};
