# 2dTD Web Host

このrepositoryは2dTD本体の第二実装ではなく、**Godot 4.6.3 standard + typed GDScript製品から生成したWeb export artifactだけを配信するstatic host**です。

ゲームルール、Save、Presentation、UIの正本は `/Users/kiku28/pj/game/2dTD/godot` 側にあります。このrepositoryではC#/.NET/Blazor実装を保持しません。

## Current source revision

`SOURCE_REVISION.txt` に、artifactを生成した2dTD本体commitを記録します。

## Static artifact

`public/`:

- `index.html`
- `index.js`
- `index.wasm`
- `index.pck`
- Godot Web runtime付属ファイル

GitHub Pagesはこの`public/`をそのまま配信します。

## Local smoke

```bash
node scripts/browser-smoke.mjs
```

Chromeで844x390のGodot Web canvasを起動し、WebGL2起動、Canvas生成、Runtime exception/errorがないことを確認します。

## Regeneration

本体repositoryで以下を実行します。

```bash
cd /Users/kiku28/pj/game/2dTD
./scripts/capture-gdscript-web-review.sh
```

その後、`.tmp/web-gdscript/`のrelease exportを本repositoryの`public/`へ同期し、`SOURCE_REVISION.txt`を更新します。Web host側でゲームロジックを修正してはいけません。
