# ObsCure-Texture-Editor
ObsCure Texture Tool is a tool for editing textures from this franchise, designed for Nintendo Wii, PC, and PS2.

## CREDITS

- HeitorSpectre  
- Evil Trainer  
- marcos_haf — HNNEWGAMES  

---

## SUPPORTED FORMATS / PLATFORMS

### Containers
- `.hvt` — Nintendo Wii / GameCube standalone texture  
- `.hvi` — PlayStation 2 standalone paletted texture  
- `.dic` — Texture dictionary (PC, PS2 RenderWare, Wii GX)  
- `.dip` — PC ObsCure 1 texture dictionary (HydraVision)  

### PC pixel formats (.dic)
- R8G8B8A8 (32 bpp)  
- R5G6B5 (16 bpp)  
- R5G5B5A1 (16 bpp)  

### PC pixel formats (.dip)
- B8G8R8A8 (32 bpp)  
- B5G6R5 (16 bpp)  
- B5G5R5A1 (16 bpp)  

### PS2 pixel formats
- PAL8 (8 bpp swizzled, RGB5551 or RGBA8888 palette)  
- RGB5551 (16 bpp, little-endian)  
- RGBA8888 (32 bpp, PS2 alpha 0..128)  

### Nintendo Wii / GameCube pixel formats
- I8 (8 bpp grayscale, GX 8x4 tiles)  
- IA8 (16 bpp grayscale + alpha, 4x4 tiles)  
- RGB5A3 (16 bpp, 4x4 tiles)  
- RGBA8 (32 bpp, 4x4 AR/GB interleaved)  
- C4 / C8 (4/8 bpp paletted with TLUT)  
- CMPR (4 bpp DXT1-style, PCA-quantized encoder)  

---

## FEATURES

- Folder-tree browser with live texture preview  
- Per-texture extract to PNG  
- Per-texture reinsert from PNG (in-place, size-checked)  
- Batch reinsert by filename matching (`.hvt` / `.hvi`)  
- Extract All from a `.dic` dictionary in one click  
