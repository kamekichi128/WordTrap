# WordTrap WebGL

EasyLocalLLM の WebGLサンプルゲームです

## 起動方法

1. このディレクトリをPowerShellで開く

2. `powershell -ExecutionPolicy Bypass -File Assets/EasyLocalLLM/Tools/prepare-webgl-output.ps1` を実行する

3. [Assets/StreamingAssets/EasyLocalLLM/models](Assets/StreamingAssets/EasyLocalLLM/models) ディレクトリを作成する

4. [HuggingFace](https://huggingface.co/Qwen/Qwen2.5-0.5B-Instruct-GGUF) から `qwen2.5-0.5b-instruct-q4_k_m.gguf` をダウンロード、上記ディレクトリに配置する

5. `File` > `Build And Run` で実行

## 参照

このプロジェクトは、[WebGLInput](https://github.com/kou-yeung/WebGLInput) を利用しています

## LICENSE

このプロジェクトはMITライセンスです