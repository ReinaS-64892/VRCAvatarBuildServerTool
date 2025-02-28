# VRCAvatarBuildServerTool

## これはなに？

これは [私](https://github.com/ReinaS-64892) が Linux にメイン環境を移行し、困った問題に対する回避策です。

### 何があったのか

すべては、私の開発する [TexTransTool](https://github.com/ReinaS-64892/TexTransTool) が Unity OpenGL にて正しく動作しないことから始まります。(それと、ドライバとの相性が悪いのか lilToon の描画も崩れてしまっていて正常に使用できないこと。)

では Vulkan に変えればいいと思いますよね！
Unity 2022.3.22f1 - Vulkan 環境は私の環境(XWayland + Wayfire + Nvidia RTX 2060SP)と相性が悪く、クラッシュして起動できなくなることも多くまともに使用できませんでした。

でも、 Unity 2022.3.59f1 - Vulkan はかなり正常に動作することがわかり、ここでは TexTransTool も何ら問題なく動作し、 lilToon の描画も崩れることもありませんでした。

少し状況を整理するとこのような状態です。

| UnityVersion  | GraphicsAPI | TexTransTool | lilToon | クラッシュ | VRCSDK Build |
| ------------- | ------------- | ------------- | ------------- | ------------- | ------------- |
| 2022.3.22f1  | OpenGL 4.5  | NG | NG | しない | 可能 |
| 2022.3.22f1  | Vulkan | OK | OK | Window PopUp などで | おそらく可能 |
| 2022.3.59f1  | OpenGL 4.5  | NG | NG | しない | 不可能 |
| 2022.3.59f1  | Vulkan  | OK | OK | しない | 不可能 |

私は当然 Editor 上で lilToon が正常に動いていてほしいですし、 TexTransTool が動作しないと話になりません。

### 解決策として

そこでこの2つの環境に着目することにしました。

- 2022.3.59f1 Vulkan -> TexTransTool が正常に動作し lilToon も正常に動作する環境
- 2022.3.22f1 OpenGL 4.5 -> VRCSDK のビルドが正常にできる

つまり、59 - Vulkan で TexTransTool をビルドし、 22 - OpenGL で VRCSDK のビルドをしてしまえばいいのです！

### 技術的な話

Http POST を用いて、ビルド対象と依存関係(Packages 配下のものを除く(一部例外あり))を zip にして サーバーに転送し、 VRCSDK の Public API を用いて受け取った zip からアップロードを行います。

詳しくは実装を見てみてください！

## 使い方

インストールは Git などを使って入れてください。

__それができないのであれば、このツールを使うに足りうるリテラシーではありません、諦めてください。((VPMなどで)容易に入れられるようにしてほしいなどの Issue などを立てられた場合 Close as not planned します。)__

### Server

適当に新規作成することを推奨します。(そのほうが VRCSDK のビルドが高速になります。)

基本は `Tools/VRCAvatarBuildServerTool/Server` から window を開き、サーバーが待ち受けるようになります。

#### インストールしておくべきパッケージ

ALCOM などを用いて VPM でインストールされているものは同時にいれる必要があります。

lilToon など Package 側にインストールされる シェーダー など。

Server側で NDMF を実行する場合は こちらにも使用するパッケージをインストールする必要があります。

### Client

アセットとしての Prefab や Scene に存在する VRCSDK がビルド可能な Prefab に対して 右クリックから `VRCAvatarBuildServerTool/BuildToServer` を実行することで、 Server 二ビルドを転送することが可能です。

#### Client設定

`Tools/VRCAvatarBuildServerTool/ClientConfiguration` から クライアントとしての設定項目が表示されます。

- BuildServerURL -> Http POST を送信する URL です。
- ClientSideNDMFExecution -> クライアント側で NDMF を実行する項目です。 サーバー側で行いたい場合はサーバー側のパッケージを確認した後に外してください。

## 注意事項

この実装は `単一 PC 内` or `LAN内`(未テスト) で行われることを想定しています。

インターネット越しで行う場合はプロキシーサーバーなどを用いて Https 化を行う必要があると思われるうえ、テストされているわけではありません。

それにそのプロキシに認証などを実装していない場合、 __安易に公開してしまえばばとても簡単に RemoteCodeExecution が行える存在です！__

私は利用規約を正しくすべて読んだわけではないためこの記述は全く持って保証できませんが、UnityEditor の機能の一部をサーバーとして使用できるようにしているため、 __他者に提供した場合利用規約違反になる可能性があります。__

__この ツール は MIT License で提供されています。なにがあっても自己責任です。ご注意ください！__
