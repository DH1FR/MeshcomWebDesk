# Translating MeshCom WebDesk

German and English are built into the source code. All other UI languages are plain JSON files, so
you can fix a translation or add a whole new language without touching any C# code.

## How it works

* The English text is the key: `"Save": "Salva"`.
* Built-in languages (currently `it`, `es`, `fr`) live in
  [`MeshcomWebDesk/Languages/`](../MeshcomWebDesk/Languages) and are embedded into the application.
* Your own files go into `<DataPath>/languages/` (the same folder that holds `appsettings.override.json`;
  the exact path is shown under **Settings → Language**). They are read at startup, or click
  **Reload language files** in Settings.
* **Docker:** the data folder is the mounted volume (`./data:/app/data` in `docker-compose.yml`), so put
  your files into `./data/languages/` on the host. The folder only needs to be readable by the container.
* A file in the data folder **overrides single strings** of a built-in language (same `code`) or **adds a new
  language** (new `code`). Everything you leave out falls back to the built-in text, then to English.
* The language list in Settings is built from the available files.

## File format

```json
{
  "code": "nl",
  "name": "Nederlands",
  "flag": "🇳🇱",
  "strings": {
    "Settings": "Instellingen",
    "{0} min ago": "{0} min geleden"
  }
}
```

* `code`: lower-case language code (`nl`, `pt-br`). `de` and `en` are reserved.
* `name`: shown in the language drop-down. `flag` is optional.
* Empty values are ignored (the English text is shown).
* Texts with `{0}`, `{1}` … are placeholders for values (callsign, count, time) – keep them in the
  translation, but you may move them.

## Finding what is missing

Developers / contributors with a checkout of the repository can run:

```
python scripts/check_translations.py            # report for all languages
python scripts/check_translations.py nl         # report for one language
python scripts/check_translations.py --write nl # add all missing keys (empty value) to Languages/nl.json
```

`--write` creates `Languages/nl.json` for a new language, listing every English text of the current
version; just fill in the values. Keys reported as *unused* are usually English texts that were reworded –
rename the key to the new wording to keep the translation.

## Contributing a language

Open a pull request that adds or updates `MeshcomWebDesk/Languages/xx.json`. Please keep the technical terms
(MH list, ext-udp, KISS, SSID, …) as they are.

Two things are not part of the JSON files: the spoken announcements (text-to-speech) exist only for
de/en/fr/it/es, and the longer guides in `docs/` are separate documents.
