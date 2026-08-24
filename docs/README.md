# Noise Filters Visual Lab

Статический учебный стенд без серверной части и внешних JavaScript-зависимостей.

**[Открыть работающую лабораторию](https://raw.githack.com/Mika-dot/Noise-filters/feature/advanced-noise-filters/docs/index.html)**

- `index.html` — разметка лаборатории;
- `styles.css` — адаптивное оформление;
- `app.js` — генератор сигналов, 13 фильтров, Canvas-график и метрики;
- `charts/` — PNG и CSV, созданные `tools/generate_charts.py`;
- `ALGORITHMS.md` — справочник по математике и параметрам.

Локальный запуск из корня репозитория:

```bash
python -m http.server 8080 --directory docs
```
