# VRCAvatarBuildServerTool

## 使い方

非常にセットアップが煩雑ですが頑張ってください。(旧版の Editor を直接 Server にする機能のことに使用したことのある人であればほぼ同じ概念が多いので簡単にわかる部分もあると思います。)

__これら、使い方はある程度高度な知識を要求します。もしそれらが理解できない場合は、このツールを使うに足りうるリテラシーではありません、諦めてください。((VPMなどで)容易に入れられるようにしてほしいなどの Issue などを立てられた場合 Close as not planned します。)__

### Server と Worker と Client

ここでは Server を、 Client とコミュニケーションを行い、ビルド転送要求を受け、Worker へと分配しビルドを行わせる、 dotnet-10 で動作するプロセスのことを指します。

Worker は、 Server からビルド指示に従い、 Server からバッチモードで起動され VRCSDK のビルドのみを行いそのままビルド終了後に自身も終了する Unity Editor のプロセスを指します。

Client は、使用者(つまりユーザー)からビルド指示操作を受け取り、指定のプレハブを一つまたは複数を Server とコミュニケーションを行いビルドを転送する、 Unity Editor のパッケージによって提供される部分を指します。

### Server & Worker

サーバー側のセットアップは複数のディレクトリ構成が可能ですが、作者が使用している構成で書きます。

まず、適当な空いてる場所にこのレポジトリをクローンしてください。(ディスク容量に空きがある場所が好ましく、 VRCSDK や Unity Editor のバグを踏まないために、 ASCII 以外の文字がパスに入らない場所のほうが好ましいです。)

次に、レポジトリ内に `ServerInstance` というディレクトリを作成してください。(`Server` というディレクトリが既に存在します。その隣りに作るように)

この `ServerInstance` を `InstanceDirectory` と今後呼称します。

`InstanceDirectory` 直下に、`config.json` というテキストファイルを生成してください。

以下のような内容を書き込む必要があります。

```json
{
    "ListenAddress": [
        "http://127.0.0.1:8080/"
    ],
    "MaxMultipleEditorCount": 4,
    "UnityEditor": "/anatano/unity/editor/innsuto-dxirekutori/2022.3.22f1/Editor/Unity",
    "ServerPasscodeHeader": "tekitounamojiretu-client-to-kyouyuu-simasu",

    "TemplateProject": "/anatano/omou/template/project/heno/path/wo/iretene",
    "RetainPackageID": [
        "net.rs64.ttce-wgpu",
        "numeira.auto-certify-copyright-agreement"
    ]
}
```

本当に、無いとは思いますが Server 側 となる UnityProject は事故った時面倒なので、新規に UnityProject (Server の TemplateProject 用)を作りましょう。(旧版で作っている人はそれを流用しても良いでしょう。)

`config.json` - `UnityEditor` の指定したバージョンと同じバージョンで Project を作りましょう。どちらも 2022.3.22f1 、つまり VRCSDK のビルドが可能なバージョンにすることを推奨します。 (VRCSDK の指定する UnityEditor のバージョンが上がったらその時はこちらも上げてください ... ! Client 側の VRCSDK のアップデートもお忘れなく)

作成した TemplateProject に VRCSDK Avatar を(現時点では) v3.10.x を導入し、VRCSDKのログイン処理をしておきましょう。(しない場合、当然アップロードに失敗します。)

また、VRChat SDK の規約的に怪しいですが ... [Auto Certify Copyright Agreement](https://github.com/Rerigferl/auto-certify-copyright-agreement) を VPM から TemplateProject にインストールしておく必要があります。

(正直 VRCSDK の想定されている用途からかなりの逸脱をしているため、これをいい形で回避できる手段が私にはわかりません。 Client 側から転送指示を出している時点で、その転送指示を出したアバターとそれに紐づくアセットはすべて許諾を持っているものとしてみなすようなものだと考えるしかありません。 私はこれに関して何ら責任を取れないので、自己責任でお願いしますね！ (そもそもこのレポジトリも MIT License ですし ...))

[Auto Certify Copyright Agreement](https://github.com/Rerigferl/auto-certify-copyright-agreement) を TemplateProject にインストールすると当時に `config.json` - `RetainPackageID` に `numeira.auto-certify-copyright-agreement` を追加しておきましょう (上記サンプルをコピペした場合は、その部分をそのままにしておくことでも問題ないでしょう。)

最後に、 `config.json` - `ServerPasscodeHeader` に適当な文字列をねじ込んどいてください、後の Client 側でも必要になるので存在を覚えておきましょう。(ちなみに、おそらく ASCII 範囲内でないと壊れると思います。(なげやり)(未検証))

#### その他細かい設定について

`ListenAddress` : 複数個入れたら別のポートからも待ち受けれるようになると思います (実装はしたが未検証)

`MaxMultipleEditorCount` : 1 以上の整数を入れてください。あなたの Server を動かす PC の性能次第で増やせば並列数が上がり、きっと、大量のビルド転送指示に対して高速にビルドが行える可能性があります。(OOM Killer には気をつけてくださいね ... ！)

`RetainPackageID` : TemplateProject にあるパッケージから、ビルド転送元の Client とパッケージを同一にする処理の時に、この配列にはいった ID のパッケージは削除されなくなります。 Client にはないが Server に入れておきたいものなどを入れておくのが良い。(通常 `numeira.auto-certify-copyright-agreement` ぐらいしかいれることはないと思いますが)

#### Server の起動

クローンしたレポジトリには `Server` というディレクトリがあり、そこで `dotnet` の実行コマンドを行うと `InstanceDirectory` として、既定で `../ServerInstance` を探しに行くようになっていて、問題がなければそのまま、起動され、ビルド転送指示の待受状態になります。

### Client

人によっては[下記参照](#textranstool--linux--nvidia-gpuユーザー向け情報)。

このレポジトリの `UnityClient` を頑張って Packages に入れてください。(`ln` をつかうのもありだよ。)

`Tools/VRCAvatarBuildServerTool/Client` から 設定 GUI を開き

- URL : `http://127.0.0.1:8080/`
- Server Passcode Header : `tekitounamojiretu-client-to-kyouyuu-simasu`

のような設定にし、Hierarchy や Project(アセットを一覧し捜索する GUI ) 右クリックから `VRCAvatarBuildServerTool/SendToBuild` を Client側 からビルドしたい Prefab に対して使用するとビルドが転送できます。

Build Server Listen Address (client側 URL) はマシン内であれば `127.0.0.1` を IP に ポート は適当に他のものと被らない物にするとよいです。
もし、マシンを超えたい場合は IP アドレスを調べ、 Build Server Listen Address (client側 URL) を `http://192.168.0.15:8080/` のような感じにすると良いです。(その時は Server 側の `config.json` - `ListenAddress` にも `http://192.168.0.15:8080/` に書き換えてください。)

ネットワークを超こえるならば ... おそらく nginx などを用いてプロキシサーバーを立てれば https にすることも可能ではあると思いますが、未検証です。やってみたい方はやってみてください。おそらく Client 側は URL に `s` を付け足すだけ、 Server側 は プロキシサーバーが渡してくれる Address にするとできるんじゃないかな ... (多分)(これも当然自己責任で本当にやってみたい人はやってみると面白いかも ... やる前に [注意事項](#注意事項) はちょっと目を通しておいてほしいけど ... )

Server Passcode (Header) を Server 側と揃えましょう。

... 　正直言っておまじないです。Server と Client で同じ文字列を入れればよいです。 ASCII 範囲外の文字を入れた時正常に動くかどうかはしりません。

#### 他ツールやパッケージに関する注意事項

`LightLimitChanger v2`(v1 は除く) のような、ライセンス認証のような機能を有する パッケージは、転送先で認証に失敗し、ビルドの妨げ or アバターにそれらのパッケージの効果が適用されない可能性があります。 (自由なものだけを使えばいいのに ... (強い思想))

VRCSDK のような認証情報の持ち方であれば、 TemplateProject にて認証を通した場合、正常にライセンス認証ができる可能性がありますし、 EditorPref などを使用するようなツールの場合は、別のプロジェクトや TemplateProject にて認証をしておくことでも解決ができるでしょう。

もし、サーバー側のみを Linux にしている場合、`可愛いポーズツール` の `可愛いポーズシステム` のような レジストリ(Win32) を使用するような認証システムを持っているツールは使用できません。(当然 Linux は Windows ではないため、レジストリ の Win32 API が使用できません。(それとも、 UnityEditor を Wine 配下で動かしますか ... ? 私はやりたくありません。))

そのツールが改変を許容しているのであるならば、ローカルでの編集でライセンス認証機能の部分を破壊することを推奨します。(このツールは Server 側に `Packages` を含めて丸ごと転送するようになっているため、手元での変更が `Packages` 配下であっても、正しく反映されます。)

もっとも、そのような不自由なライセンスを課すツールが、ローカルでの編集を許容していることは(なくはないですが)珍しく、プロジェクトを複数に分けるような人は導入に手間がかかるようになるので、捨ててしまって、ライセンス認証のない自由なツールに乗り換えてしまったほうが幸せだと私は思います。

(このツールのような、むちゃくちゃなことを行う時に牙を向くのはいつも不自由な道具ですが、それもまた、「不自由なソフトウェアに依存した当然の報い」なのかもしれませんね ... () )

#### ビルド転送指示について

##### 対象サーバーの決定

Client の設定、 BuildServers に複数サーバーを登録できることにはお気づきでしょう。これは複数の対象を切り替えやすくするために複数個入力できるようになっており、そのうちの一番最初のサーバーにビルドが転送されるようになっています。

(切り替えたい場合は適宜、登録したサーバーをDragし、順序を入れ替えてください。)

##### ビルド対象アバターの決定

Project(アセットを一覧し捜索する GUI) 右クリックから `VRCAvatarBuildServerTool/SendToBuild` ビルドの転送指示が可能ですが ... この場合、選択状態によって挙動が変わります。

何も選択していない状態では、 `VRCAvatarBuildServerTool/SendToBuild/Others/BuildToServer-from-Label` に相当する操作となり、何かを選択している場合は、`VRCAvatarBuildServerTool/Others/BuildToServer-from-Selection` に相当する操作になります。

(ちなみに Project(GUI) では、何も選択していない状態にみえて、実際にはフォルダを選択しているようで、何も選択していない状態の判定には DefaultAsset を一つだけ選択している状態もその判定に含まれています。)

###### from-Label

Unity の標準機能で Asset には ラベルと呼ばれる文字列を付与することができ、それを基準で、プロジェクト全体で収集し、一度にたくさんのビルド転送指示を行えます。

(私の環境では CAU の SettingGroups が度々 UnityEditor のフリーズを引き起こすので、追加されました。(おそらくこの現象事態は Linux の UnityEditor によって発生する「おま環」だと思われるため対処していません。(なお、そうであるかは未検証です。)))

また、`VRCAvatarBuildServerTool/Others/Add-BuildTarget-Windows-Label` から Windows 向けビルドのビルド転送指示を行うようにラベルを付与し、`VRCAvatarBuildServerTool/Others/Add-BuildTarget-Mobile-Label` から Android と IOS 向けビルドのビルド転送指示を行うようにラベルを付与するヘルパーがあります。
(既に付与されている Prefab がプロジェクトに存在する場合は、候補に表示されるので、これを常に使用する必要はなく、ラベルを UnityEditor 標準の UI から付与することでも同等の効果を持ちます。)

###### from-Selection

Project(アセットを一覧し捜索する GUI) にて UnityEditor は選択を使用することが可能で、選択している対象を基に、ビルド指示を行います。

この時、複数個選択している場合はすべてを対象に、現在の Client となる UnityEditor が対象としているビルドターゲットに対応したプラットフォーム向けのビルド転送指示を行います。

また、この時、[ContinuousAvatarUploader](https://github.com/anatawa12/ContinuousAvatarUploader) の CAU AvatarUploadSettingGroup が選択に入っている場合、その CAU AvatarUploadSettingGroup の中身もビルド転送指示に入れる機能があり、その場合には 現在の Client となる UnityEditor が対象としているビルドターゲット を完全に無視し、CAU AvatarUploadSettingGroup に入力されているターゲットプラットフォームに応じて、それぞれのプラットフォーム向けのビルド指示が行われます。

###### Hierarchy

Scene にある Prefab を一覧で閲覧することのできる `Hierarchy` にも`VRCAvatarBuildServerTool/SendToBuild`は使用可能で、こちらの場合は常に `VRCAvatarBuildServerTool/Others/BuildToServer-from-Selection` に相当する操作になります。

(ただし、 Scene 上にあるものを対象としているため、内部挙動などは完全には同一ではないことにはご注意を。)

### TexTransTool & Linux & Nvidia GPUユーザー向け情報

__注意事項__ この情報は古いです。

`-force-vulkan` を追加すると同時に `-force-gfx-direct` を引数に追加するとクラッシュしなくなります。こちらを使ったほうが良いと思われます。

参考 : https://discord.com/channels/1065291958328758352/1065291958794322012/1456196419949039751

[何があったのか](#何があったのか) に具体的に細かく書いていますが TTT のためには、手元で作業する UnityEditor は Vulkan で動かす必要があります。
TexTransTool は ComputeShader で動作するため OpenGL と基本的に相性が悪いため。

#### Client 推奨

UnityEditor Version 2022.3.59f1 or later

command line option `-force-vulkan`

一応 ... Unity6 とかにしても動きそうですが、Unity 側のファイルの保存形式が変わってたりすると壊れうるので同一メジャー(?)バージョンの範囲にすることを推奨します。

Vulkan で起動すると、TTT が正常に動作し Preview できるようになります。普段はこちらでアバター改変などを行いましょう。

#### Server & Worker - TemplateProject 推奨

UnityEditor Version 2022.3.22f1

command line option `` (何も入れない)

VRCSDK の制限によりこのバージョンしか使えません。なお 22f1 を NvidiaGPU で Vulkan 指定で起動すると即クラッシュして死にます。 OpenGL しか使えません。

つまり、 NvidiaGPU + OpenGL 環境では TTT だけではビルドができません。下記の抜け道(TTCE-Wgpu)を使用しない場合はビルド転送時に `ClientSideNDMFExecution` のついた方を使用してください。

### TTCE-Wgpu

OpenGL を使用している UnityEditor でも、自らの力で Vulkan のコンテキストを掴み取れば Vulkan を使用することができます！

そして、TexTransTool は UnityEngine 以外を GPU バックエンドとして使用することが可能で、そのバックエンド実装の一つが [TTCE-Wgpu](https://github.com/ReinaS-64892/TTCE-Wgpu) となります。

TemplateProject に TTCE-Wgpu を VPM からインストールした後、 TTT の設定メニューから `Experimental` `TexTransCoreEngine` を `Unity` から `Wgpu` にすることで使用することができます。

これで無理やり NvidiaGPU + Unity 2022.3.22f1 (OpenGL) で TTT をビルドできるので、興味があるならやってみてください。

... おそらく `ClientSideNDMFExecution` を使うほうが安定すると思います。

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

### 追記2

[要山くろね](https://x.com/kuroclef)氏から、`-force-gfx-direct` を引数に追加すると、 vulkan を使用させている時にクラッシュしなくなるとの[情報](https://discord.com/channels/1065291958328758352/1065291958794322012/1456196419949039751)をもらいました。

実際 KDE-Plasma + RTX2060SP + 2022.3.22f1 + Vulkan 環境にてクラッシュしませんでした。もうこれでいいね。このツールはワークアラウンドのためにはらないってことで。

### 技術的な話

Server がキャッシュを持ち、ビルドリクエストの HTTP Post をし、キャッシュが足りなければ、 Client がファイルを HTTP Post しまくり、ファイルが足りれば、そのビルドリクエストを受け付け、ビルドするようになっています。

詳しくは実装を見てみてください！
