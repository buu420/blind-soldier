"""Generate REVIEW DRAFTS using pinned Argos packages; never used by the game.

The initial drafts required extensive semantic corrections. This tool does not
reproduce the curated shipping translations and refuses their source directory.
Review every generated entry before making any deliberate catalog changes.

Dependencies: ctranslate2 4.7.1, sentencepiece 0.2.1, sacremoses 0.1.1,
subword-nmt 0.3.8. Model URLs/hashes are recorded beside generated catalogs.
Export texts using the x86 tests' --export-description-texts PATH first.
"""
import argparse
import concurrent.futures
import hashlib
import json
import pathlib
import re
import time

import ctranslate2
import sentencepiece
from sacremoses import MosesDetokenizer, MosesPunctNormalizer, MosesTokenizer
from subword_nmt.apply_bpe import BPE

NAMES = ["Red XIII", "Cait Sith", "Ancient Forest", "Huge Materia", "Knights of the Round",
         "Highwind", "Sephiroth", "Shinra", "Jenova", "JENOVA", "Aerith", "Aeris",
         "Cloud", "Tifa", "Barret", "Vincent", "Yuffie", "Cid", "Nanaki", "Marlene",
         "Elmyra", "Priscilla", "Reeve", "Rufus", "Reno", "Rude", "Tseng", "Elena",
         "Bugenhagen", "Lucrecia", "Hojo", "Zack", "Midgar", "Nibelheim", "Wutai",
         "Junon", "Gelnika", "Gongaga", "Mideel", "Cosmo Canyon", "Red Thirteen",
         "Meteor", "Holy", "Lifestream", "Materia", "materia"]
NAME_PATTERN = re.compile(r"\b(?:" + "|".join(map(re.escape, NAMES)) + r")\b")
JAPANESE = {"Cloud": "クラウド", "Tifa": "ティファ", "Barret": "バレット",
            "Aerith": "エアリス", "Aeris": "エアリス", "Sephiroth": "セフィロス",
            "Shinra": "神羅", "Jenova": "ジェノバ", "JENOVA": "ジェノバ", "Cid": "シド",
            "Vincent": "ヴィンセント", "Yuffie": "ユフィ", "Red XIII": "レッドXIII",
            "Cait Sith": "ケット・シー", "Midgar": "ミッドガル", "Highwind": "ハイウインド"}
TERMS = {
    "fr": {"Meteor": "le Météore", "Holy": "le Sacré", "Lifestream": "la Rivière de la vie", "Materia": "Matéria", "materia": "matéria"},
    "de": {"Meteor": "Meteor", "Holy": "Heilig", "Lifestream": "Lebensstrom", "Materia": "Substanz", "materia": "Substanz"},
    "es": {"Meteor": "Meteorito", "Holy": "Sagrado", "Lifestream": "Corriente Vital", "Materia": "Materia", "materia": "materia"},
    "ja": {**JAPANESE, "Red Thirteen": "レッドXIII", "Meteor": "メテオ", "Holy": "ホーリー", "Lifestream": "ライフストリーム", "Materia": "マテリア", "materia": "マテリア"},
    "pl": {"Meteor": "Meteor", "Holy": "Świętość", "Lifestream": "Strumień Życia", "Materia": "Materia", "materia": "materia"}}


def protect(text):
    return NAME_PATTERN.sub(lambda match: f"ZXQ{NAMES.index(match[0])}XQZ", text)


class Codec:
    def __init__(self, directory, language):
        self.sp = None
        if (directory / "sentencepiece.model").exists():
            self.sp = sentencepiece.SentencePieceProcessor(str(directory / "sentencepiece.model"))
        else:
            self.normalizer = MosesPunctNormalizer("en")
            self.tokenizer = MosesTokenizer("en")
            self.detokenizer = MosesDetokenizer(language)
            with (directory / "bpe.model").open(encoding="utf-8") as stream:
                self.bpe = BPE(stream)

    def encode(self, text):
        if self.sp:
            return self.sp.encode(text, out_type=str)
        return self.bpe.segment_tokens(self.tokenizer.tokenize(self.normalizer.normalize(text)))

    def decode(self, tokens):
        if self.sp:
            return self.sp.decode(tokens).replace("▁", " ").strip()
        return self.detokenizer.detokenize(" ".join(tokens).replace("@@ ", "").split())


def generate(code, texts, models, output):
    model = next((models / code).glob("*/model"))
    codec = Codec(model.parent, code)
    translator = ctranslate2.Translator(str(model), device="cpu", compute_type="int8",
                                       inter_threads=1, intra_threads=3)
    path = output / (code + ".json")
    catalog = json.loads(path.read_text(encoding="utf-8")) if path.exists() else {}
    pending = [text for text in texts if text not in catalog]
    issues = []
    start = time.time()
    for offset in range(0, len(pending), 24):
        batch = pending[offset:offset + 24]
        segments = [re.split(r"(?<=[.!?])\s+", text) for text in batch]
        flat = []
        for parts in segments:
            for part in parts:
                # Dialogue included in reviewed movie paragraphs retains its speaker.
                match = re.match(r"^(" + "|".join(map(re.escape, NAMES)) + r"): (.*)$", part)
                flat.append((match[1] + ": " if match else "", match[2] if match else part))
        results = translator.translate_batch([codec.encode(protect(text)) for _, text in flat],
                                            beam_size=4, max_input_length=1024,
                                            max_decoding_length=512)
        translated_parts = iter(prefix + codec.decode(result.hypotheses[0])
                                for (prefix, _), result in zip(flat, results))
        for original, parts in zip(batch, segments):
            translated = " ".join(next(translated_parts) for _ in parts)
            required = set(NAME_PATTERN.findall(original))
            for name in required:
                token = f"ZXQ{NAMES.index(name)}XQZ"
                if token.casefold() not in translated.casefold() and name + ": " not in translated:
                    issues.append({"source": original, "result": translated, "missing": name})
                replacement = TERMS.get(code, {}).get(name, name)
                translated = re.sub(re.escape(token), lambda _: replacement, translated, flags=re.IGNORECASE)
                if code == "ja":
                    translated = translated.replace(name + ": ", TERMS[code].get(name, name) + ": ")
            catalog[original] = translated
        temporary = path.with_suffix(".tmp")
        temporary.write_text(json.dumps(dict(sorted(catalog.items())), ensure_ascii=False, indent=2) + "\n",
                             encoding="utf-8")
        temporary.replace(path)
        if offset % 240 == 0:
            print(f"{code}: {min(offset + 24, len(pending))}/{len(pending)}, {time.time() - start:.1f}s", flush=True)
    (output / (code + "-review.json")).write_text(json.dumps(issues, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"{code}: finished {len(catalog)} texts, {len(issues)} glossary checks to review", flush=True)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--texts", required=True, type=pathlib.Path)
    parser.add_argument("--models", required=True, type=pathlib.Path)
    parser.add_argument("--out", required=True, type=pathlib.Path)
    parser.add_argument("--languages", default="fr,de,es,ja,pl")
    args = parser.parse_args()
    args.out = args.out.resolve()
    shipping = pathlib.Path(__file__).resolve().parents[2] / "Ff7.Accessibility.LegacyLayout" / "AudioDescriptionLocalization"
    if args.out == shipping or shipping in args.out.parents or args.out in shipping.parents:
        parser.error("Draft generation cannot overwrite the curated shipping catalogs. Choose a separate scratch directory.")
    texts = json.loads(args.texts.read_text(encoding="utf-8-sig"))
    assert texts and len(set(texts)) == len(texts)
    args.out.mkdir(parents=True, exist_ok=True)
    (args.out / "en.json").write_text(json.dumps({text: text for text in texts}, ensure_ascii=False, indent=2) + "\n",
                                     encoding="utf-8")
    with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
        futures = [pool.submit(generate, code, texts, args.models, args.out)
                   for code in args.languages.split(",")]
        for future in futures:
            future.result()
    provenance = json.loads((args.models / "provenance.json").read_text(encoding="utf-8"))
    (args.out / "generation.json").write_text(json.dumps({"textsSha256": hashlib.sha256(args.texts.read_bytes()).hexdigest(),
        "textCount": len(texts), "method": "offline Argos models, int8 CPU, beam 4, sentence boundaries; protected character/place names",
        "models": provenance}, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
