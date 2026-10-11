# Third-party notices

The plugin package includes:

| Component | Use | License |
|---|---|---|
| PP-OCR text detection model (`src/Jellyfin.Plugin.TitleCards/Models/text-detection.onnx`), from [PaddleOCR](https://github.com/PaddlePaddle/PaddleOCR) via [RapidOCR](https://github.com/RapidAI/RapidOCR)'s ONNX conversion (`PP-OCRv6_det_small.onnx`) | Finding text in artwork | Apache License 2.0 |
| [ONNX Runtime](https://github.com/microsoft/onnxruntime) (`Microsoft.ML.OnnxRuntime.dll`, `libonnxruntime.so` for Linux x64 and ARM64) | Running the detection model | MIT License |
| `System.Numerics.Tensors.dll` (.NET) | ONNX Runtime dependency | MIT License |

Used from the Jellyfin server at runtime, not shipped: SkiaSharp (MIT), Jellyfin libraries (GPL).

Development scripts only, not shipped: `build/make_parity_fixtures.py` and `build/render_examples.py`
use Pillow (MIT-CMU), RapidOCR (Apache-2.0) and ONNX Runtime (MIT), installed separately with pip.
`render_examples.py` draws the Live TV icon with Google's Material Icons font (Apache-2.0), taken
from jellyfin-web's dependencies.
