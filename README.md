# VRCAvatarBuildServerTool

## 使い方

インストールは Git などを使って入れてください。

__それができないのであれば、このツールを使うに足りうるリテラシーではありません、諦めてください。((VPMなどで)容易に入れられるようにしてほしいなどの Issue などを立てられた場合 Close as not planned します。)__

## Server と Client

ここでは Server を VRCSDK のビルドを行う側(転送先)、Client が VRCSDK のビルドを押し付けようとする側(転送元)としています。

## セットアップ

### Server

新規に UnityProject (Server用)を作りましょう。無いとは思いますが Server 側 となる UnityProject は事故った時面倒なので Server 側となる Project を作りましょう。また、この時 UnityVersion は 2022.3.22f1 、つまり VRCSDK のビルドが可能なバージョンにしましょう。

作成した UnityProject に VRCSDK Avatar を(現時点では) v3.9.0-beta.1 を導入し、次に、このレポジトリを `Packages` に git clone してください。

また、VRChat SDK の規約的に怪しいですが、楽をしたいのであれば [Auto Certify Copyright Agreement](https://github.com/Rerigferl/auto-certify-copyright-agreement) を同時に VPM からインストールしておくと Server として使いやすくなります。(私はこれに関して何ら責任を取れないので、自己責任で(そもそもこのレポジトリも MIT License ですし ...))

### Client

人によっては[下記参照](#textranstool--linux--nvidia-gpuユーザー向け情報)。

このレポジトリを `Packages` に git clone してください。

### 繋ぐ

- Server 側
  - Build Server Listen Address : `http://127.0.0.1:8080/`
  - Server Passcode : `tekitounamojiretu`
- Client 側
  - Element 0
    - Enable : [x]
    - URL : `http://127.0.0.1:8080/`
    - Server Passcode Header : `tekitounamojiretu`

のような設定にし、Hierarchy 右クリックから `VRCAvatarBuildServerTool/SendToBuild` を Client側 からビルドしたい Prefab に対して使用するとビルドが転送できます。

Build Server Listen Address (client側 URL) はマシン内であれば `127.0.0.1` を IP に ポート は適当に他のものと被らない物にするとよいです。
もし、マシンを超えたい場合は IP アドレスを調べ、 Build Server Listen Address (client側 URL) を http://192.168.0.15:8080/ のような感じにすると良いです。

ネットワークを超こえるならば ... おそらく nginx などを用いてプロキシサーバーを立てれば https にすることも可能ではあると思いますが、未検証です。やってみたい方はやってみてください。おそらく Client 側は URL に `s` を付け足すだけ、 Server側 は プロキシサーバーが渡してくれる Address にするとできるんじゃないかな ... (多分)(これも当然自己責任で本当にやってみたい人はやってみると面白いかも ... やる前に [注意事項](#注意事項) はちょっと目を通しておいてほしいけど ... )

Server Passcode (Header) は ... おまじないです。Server と Client で同じ文字列を入れればよいです。 ASCII 範囲外の文字を入れた時正常に動くかどうかはしりません。

### TexTransTool & Linux & Nvidia GPUユーザー向け情報

[何があったのか](#何があったのか) に具体的に細かく書いていますが TTT のためには、手元で作業する UnityEditor は Vulkan で動かす必要があります。
TexTransTool は ComputeShader で動作するため OpenGL と基本的に相性が悪いため。

#### Client 推奨

UnityEditor Version 2022.3.59f1 or later

command line option `-force-vulkan`

一応 ... Unity6 とかにしても動きそうですが、Unity 側のファイルの保存形式が変わってたりすると壊れうるので同一メジャー(?)バージョンの範囲にすることを推奨します。

Vulkan で起動すると、TTT が正常に動作し Preview できるようになります。普段はこちらでアバター改変などを行いましょう。

#### Server 推奨

UnityEditor Version 2022.3.22f1

command line option `` (何も入れない)

VRCSDK の制限によりこのバージョンしか使えません。なお 22f1 を NvidiaGPU で Vulkan 指定で起動すると即クラッシュして死にます。 OpenGL しか使えません。

つまり、 NvidiaGPU + OpenGL 環境では TTT だけではビルドができません。下記の抜け道(TTCE-Wgpu)を使用しない場合はビルド転送時に `ClientSideNDMFExecution` のついた方を使用してください。

### TTCE-Wgpu

OpenGL を使用している UnityEditor でも、自らの力で Vulkan のコンテキストを掴み取れば Vulkan を使用することができます！

そして、TexTransTool は UnityEngine 以外を GPU バックエンドとして使用することが可能で、そのバックエンド実装の一つが [TTCE-Wgpu](https://github.com/ReinaS-64892/TTCE-Wgpu) となります。

TTCE-Wgpu を VPM からインストールした後、 TTT の設定メニューから `Experimental` `TexTransCoreEngine` を `Unity` から `Wgpu` にすることで使用することができます。

これで無理やり NvidiaGPU + Unity 2022.3.22f1 (OpenGL) で TTT をビルドできるので、興味があるならやってみてください。

... おそらく `ClientSideNDMFExecution` を使うほうが安定すると思います。

### ClientSideNDMFExecution

## 注意事項

この実装は `単一 PC 内` or `LAN内`(未テスト) で行われることを想定しています。

インターネット越しで行う場合はプロキシーサーバーなどを用いて Https 化を行う必要があると思われるうえ、テストされているわけではありません。

それにそのプロキシに認証などを実装していない場合、 __安易に公開してしまえばばとても簡単に RemoteCodeExecution が行える存在です！__

私は利用規約を正しくすべて読んだわけではないためこの記述は全く持って保証できませんが、UnityEditor の機能の一部をサーバーとして使用できるようにしているため、 __他者に提供した場合利用規約違反になる可能性があります。__

__この ツール は MIT License で提供されています。なにがあっても自己責任です。ご注意ください！__

## これはなぜ作られた？

これは [私](https://github.com/ReinaS-64892) が Linux にメイン環境を移行し、Nvidia GPU を使用していたがために、困った問題に対する回避策として作られました。

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

### 追記

私の環境がそれから `XWayland + Wayfire + Radeon RX 6700XT` になったため、`2022.3.22f1 OpenGL 4.5` で動くかなと思ったのですが、 TTT の取扱に Radeon の OpenGL ComputeShader 実装が耐えられないようでこのツールを捨てることはできなかったようです。(当然 `2022.3.59f1 Vulkan` は正常ビルドが可能ですが、 `XWayland + Randeon GPU` 環境でしか発生しないバグを踏み抜くなどがあり `2022.3.39f1 Vulkan` の環境で私は作業しています。)

私がサーバー側の環境を整えたこともあり、元々使用していた `Nvidia RTX 2060SP` をサーバー側に挿し、そして、手元の環境で TexTransTool のビルドを回避したいがために、 TTCE-Wgpu を使用することで、 TTT を `2022.3.22f1 OpenGL 4.5` の上でゴリ押しでどうさせれるようにしたため、私の環境は現在このようになっています。

| GPU | UnityVersion  | GraphicsAPI | TexTransTool (TTCE-Wgpu) | lilToon | クラッシュ | VRCSDK Build |
| ------------- | ------------- | ------------- | ------------- | ------------- | ------------- | ------------- |
| Nvidia RTX 2060SP | 2022.3.22f1  | OpenGL 4.5  | NG (OK) | OK | しない | 可能 |
| Radeon RX 6700XT | 2022.3.39f1  | Vulkan  | OK (OK) | OK | しない | 不可能 |

ちなみに、このツールはサーバーへのビルド転送の受信を別スレッドで行っているので、 ClientSideNDMFExecution をオフにすれば連続で（サーバー側でビルド中であっても）送れるようになっていて、とってもお得！

### 技術的な話

Server がキャッシュを持ち、ビルドリクエストの HTTP Post をし、キャッシュが足りなければ、 Client がファイルを HTTP Post しまくり、ファイルが足りれば、そのビルドリクエストを受け付け、ビルドするようになっています。

詳しくは実装を見てみてください！
