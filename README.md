# ObsCure-Texture-Editor
ObsCure Texture Editor is a tool for editing textures from this franchise, designed for Nintendo Wii, PC, PS2, PS3 and XBOX.

## SUPPORTED GAMES

- ObsCure I
- Obscure II (Obscure: The Aftermath)
- Final Exam

## CREDITS

- [HeitorSpectre](https://github.com/HeitorSpectre)
- [Evil Trainer ](https://github.com/evil-trainer) 
- [marcos_haf — HNNEWGAMES  ](https://github.com/marcoshaf)

---

## IMAGES
<img width="1184" height="792" alt="image" src="https://github.com/user-attachments/assets/9c55393d-606e-4b74-9c86-2c9db8071e14" />

<img width="1184" height="789" alt="image" src="https://github.com/user-attachments/assets/a4a89905-65fd-46cc-8ecf-961182d2247b" />



## SUPPORTED FORMATS / PLATFORMS

### Containers
- `.hvt` — Nintendo Wii / GameCube standalone texture
- `.hvt` — Final Exam PC / PS3 / Xbox 360 (HydraVision modern) standalone
- `.hvi` — PlayStation 2 standalone paletted texture  
- `.dic` — Texture dictionary (PC, PS2 RenderWare, Wii GX)  
- `.dip` — PC ObsCure 1 texture dictionary (HydraVision)
- `.xbr` — Xbox classic texture dictionary (NV2A swizzled) 

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

###   Xbox classic pixel formats (.xbr, NV2A swizzled)
- SZ_R5G6B5    (16 bpp, Morton-order swizzle)
- SZ_A1R5G5B5  (16 bpp, Morton-order swizzle)
- SZ_A8R8G8B8  (32 bpp, Morton-order swizzle)

###     Final Exam pixel formats (.hvt, PC "HVI " / PS3+X360 " IVH")
- BGRA      (PC, 32 bpp linear)
- BGRX      (PC, 32 bpp linear, alpha forced opaque)
- TXD1/DXT1 (PC+PS3, BC1, 4 bpp)
- TXD3/DXT3 (PC+PS3, BC2, 8 bpp)
- TXD5/DXT5 (PC+PS3, BC3, 8 bpp)
- ARGB      (PS3 32 bpp linear / X360 32 bpp 32×32 tiled)
- PS3+X360 headers are big-endian; BC blocks stay as PC LE.
