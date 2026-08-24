# Filter comparison

```mermaid
xychart-beta
 title "Noisy signal vs filtering"
 x-axis [1,2,3,4,5,6,7,8,9,10]
 y-axis "value" 0 --> 10
 line "Raw" [2,9,3,8,4,10,2,8,5,9]
 line "Filtered" [4,5,5,6,6,7,6,7,7,8]
```

## Behavior

| Filter | Removes spikes | Preserves shape | Response |
|---|---|---|---|
| Moving average | medium | medium | slow |
| Median | excellent | good | medium |
| Kalman | good | excellent | adaptive |
| Hampel | excellent | excellent | event based |
