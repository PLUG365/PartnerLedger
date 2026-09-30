# PartnerLedger Design Guide

版：0.3 / 2026-09-10

参照： [PPDevStandard](https://github.com/minoru365/PPDevStandard) および [CodeAppsDevelopmentStandard](https://github.com/geekfujiwara/CodeAppsDevelopmentStandard)。Code Apps標準のデザイン言語、コンポーネント再利用、レスポンシブ設計、一覧→詳細の導線をPartnerLedger向けに絞っている。

## Design direction

PartnerLedgerは、日常的に使う業務台帳として「情報を速く見つけられる」「状態と期限が一目で分かる」「操作の結果が予測できる」ことを優先する。

視覚言語は、既存の [DecisionFlow](https://github.com/PLUG365/DecisionFlow) と並べて使える白・淡い青・鮮やかな青を基調にする。Material 3／Tailwind CSS系の明確な階層、フォーカス、状態表現と、控えめな境界線・影を組み合わせる。DecisionFlow本体や別リポジトリは変更せず、PartnerLedger側のデザインだけを整合させる。装飾的なコピー、ポエム調の見出し、過度なグラデーション、強いカード装飾は使わない。画面文言は機能、状態、次の操作を優先する。

## Colour palette

| Token | Hex | Usage |
| --- | --- | --- |
| Primary | `#3B82F6` | 主操作、選択状態、フォーカス |
| Primary dark | `#1D4ED8` | 主操作のhover、濃い文字 |
| Primary hover | `#2563EB` | 主操作のhover、リンク |
| Primary soft | `#DBEAFE` | 選択行、補助面、アイコン背景 |
| Secondary | `#0F766E` | 補助操作、正常状態 |
| Accent | `#F59E0B` | 期限、注意 |
| Background | `#EFF6FF` | アプリ背景 |
| Surface | `#FFFFFF` | パネル、入力、カード |
| Surface muted | `#F0F7FF` | サイドバー、ツールバー、表ヘッダー |
| Border | `#BFDBFE` | 境界線 |
| Border strong | `#93C5FD` | フォーカス、操作対象の境界線 |
| Text | `#0F2F5F` | 本文、見出し |
| Text secondary | `#365579` | 補足、説明 |
| Text muted | `#64748B` | ラベル、メタ情報 |
| Success | `#15803D` | 完了、在籍、取引中 |
| Warning | `#B45309` | 期限、確認待ち |
| Error | `#B91C1C` | 失敗、エラー |
| Info | `#0369A1` | 同期、情報 |

## Typography

- Font family: `system-ui`, `-apple-system`, `BlinkMacSystemFont`, `Segoe UI`, `Hiragino Sans`, `Yu Gothic UI`, `Meiryo`, sans-serif。Code Appsでは外部フォントを必須にしない。
- Page title: 30px / 700 / 1.25
- Section heading: 18px / 700 / 1.4
- Body: 14px / 400 / 1.6
- Label: 12px / 600 / 1.4
- Caption: 11px / 400 / 1.5

日本語の可読性を優先し、英字だけを理由に外部フォントを必須にしない。

## Spacing and layout

- 4px基準のスペーシングスケールを使う。
- PCのサイドバーは展開時240px、折り畳み時76px。
- コンテンツの最大幅は1600px。画面端から24〜48pxを確保する。
- パネル間は16〜24px。入力欄とボタンの高さは40px前後で揃える。
- 画面幅850px未満ではサイドバーを上部ナビゲーションへ切り替える。

## Components

- Buttons: 8px radius。主操作はPrimary、補助操作は白背景＋Border。危険操作は赤系で、色だけに依存しない。
- Cards: 白背景、1pxのBorder、控えめなShadow、12〜16px radius。
- Navigation: 淡い青の左サイドバー。選択項目はPrimaryの青い塗りと白文字で示す。ヘッダーにはPartnerLedgerのロゴとアプリ名を常に表示し、折り畳み時も`title`と`aria-label`を維持する。
- Icons: 文字記号や自作の図形ではなく、[Lucide](https://lucide.dev/)の同じ線幅・同じ光学サイズのアイコンを使う。折り畳み操作はサイドパネルの開閉を表すアイコンと`aria-expanded`で示す。
- Tables: ヘッダーを淡い青面にし、行のhoverと選択状態を区別する。状態はラベルと色を併用する。詳細は常設の右パネルにせず、行末のアイコンから左側ドロワーを開く。
- Forms: ラベルを入力上部に置き、必須項目を明示する。保存・申請・確認の意味をボタン文言で分ける。

## Information architecture

左メニューは「ホーム」「新規登録」「取引先リスト」「担当者リスト」「アーカイブ」「設定」。名刺登録は「新規登録」の選択画面から開く。会社登録と担当者登録はフォームを分離するが、入口は新規登録にまとめる。

ホームは集計を眺めるだけのダッシュボードにせず、要対応一覧と各業務画面へのリンクを持つ。取引先リストは検索・登録者フィルター付きのマスター一覧と、行末アイコンから開く詳細ドロワーで構成する。長い会社名・メールアドレスは親要素の幅を制約し、ページ全体の横スクロールを発生させない。

## Accessibility

- 本文と背景はWCAG AA相当を目標にする。
- フォーカスリングを常に表示する。
- 状態は色、ラベル、必要に応じてアイコンの3要素で伝える。
- 折り畳みナビゲーションのアイコンには`title`と`aria-label`を付ける。

## Implementation contract

実装時は`apps/pc/src/styles/design-tokens.css`の変数を使用し、コンポーネント内へ新しい色・余白・影を直接埋め込まない。業務状態、承認ルール、権限表示、オフライン制約はこの見た目のガイドより`AGENTS.md`と正本ドキュメントを優先する。
