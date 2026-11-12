---
name: Fluent ASM
colors:
  surface: '#fcf9f8'
  surface-dim: '#dcd9d9'
  surface-bright: '#fcf9f8'
  surface-container-lowest: '#ffffff'
  surface-container-low: '#f6f3f2'
  surface-container: '#f0eded'
  surface-container-high: '#eae7e7'
  surface-container-highest: '#e5e2e1'
  on-surface: '#1b1c1c'
  on-surface-variant: '#3e484e'
  inverse-surface: '#303030'
  inverse-on-surface: '#f3f0ef'
  outline: '#6e797f'
  outline-variant: '#bdc8cf'
  surface-tint: '#006687'
  primary: '#006687'
  on-primary: '#ffffff'
  primary-container: '#60cdff'
  on-primary-container: '#005572'
  inverse-primary: '#74d1ff'
  secondary: '#00658d'
  on-secondary: '#ffffff'
  secondary-container: '#4bc1fe'
  on-secondary-container: '#004d6c'
  tertiary: '#7f543b'
  on-tertiary: '#ffffff'
  tertiary-container: '#edb596'
  on-tertiary-container: '#6e452d'
  error: '#ba1a1a'
  on-error: '#ffffff'
  error-container: '#ffdad6'
  on-error-container: '#93000a'
  primary-fixed: '#c1e8ff'
  primary-fixed-dim: '#74d1ff'
  on-primary-fixed: '#001e2b'
  on-primary-fixed-variant: '#004d67'
  secondary-fixed: '#c6e7ff'
  secondary-fixed-dim: '#81cfff'
  on-secondary-fixed: '#001e2d'
  on-secondary-fixed-variant: '#004c6b'
  tertiary-fixed: '#ffdbc9'
  tertiary-fixed-dim: '#f3ba9b'
  on-tertiary-fixed: '#311302'
  on-tertiary-fixed-variant: '#643d26'
  background: '#fcf9f8'
  on-background: '#1b1c1c'
  surface-variant: '#e5e2e1'
typography:
  headline-sm:
    fontFamily: Segoe UI Variable
    fontSize: 14px
    fontWeight: '600'
    lineHeight: 20px
  body-code:
    fontFamily: Cascadia Code
    fontSize: 13px
    fontWeight: '400'
    lineHeight: 22px
    letterSpacing: 0px
  body-code-bold:
    fontFamily: Cascadia Code
    fontSize: 13px
    fontWeight: '600'
    lineHeight: 22px
  gutter-number:
    fontFamily: Cascadia Code
    fontSize: 12px
    fontWeight: '400'
    lineHeight: 22px
  status-label:
    fontFamily: Segoe UI Variable
    fontSize: 12px
    fontWeight: '400'
    lineHeight: 16px
    letterSpacing: 0.1px
  status-value:
    fontFamily: Cascadia Code
    fontSize: 11px
    fontWeight: '500'
    lineHeight: 16px
rounded:
  sm: 0.125rem
  DEFAULT: 0.25rem
  md: 0.375rem
  lg: 0.5rem
  xl: 0.75rem
  full: 9999px
spacing:
  gutter: 0px
  margin: 0px
  space-xs: 4px
  space-sm: 8px
  space-md: 12px
  space-lg: 16px
  space-xl: 24px
---

## Brand & Style

This design system delivers a distraction-free, native Windows App SDK / WinUI 3 experience specifically engineered for low-level systems programmers, reverse engineers, and assembly developers (x86_64 / ARM). 

The emotional signature is quiet precision, structural discipline, and extreme focus. By stripping away traditional IDE clutter—menus, toolbars, project drawers, and tab ribbons—the interface recedes completely, treating low-level assembly instruction streams as primary art.

The visual style adheres strictly to Windows 11 Fluent Design principles:
- **Mica Alt Materiality:** Deep layered desktop composition utilizing translucent desktop tinting.
- **Architectural Edge Definition:** Precise 1px micro-borders rendered with low-contrast system strokes (`CardStrokeColorDefault`).
- **Monastic Utility:** Clean, purposeful interactions with zero ornamental noise. All user engagement centers entirely on the code buffer and an ambient status telemetry line.

## Colors

The palette is tuned for the Windows 11 light mode desktop ecosystem, emphasizing clean surface layering and an expressive, high-contrast syntax highlighting spectrum tailored for assembly language tokens (opcodes, registers, immediate values, directives, labels, and memory operands).

### Surfaces & Chrome
- **Mica Alt Background Base:** `#F3F3F3` with system composite light drop.
- **Canvas Base (Editor Workspace):** `#FFFFFF` with high contrast against light backdrop.
- **Active Line Treatment:** `#E5E5E5` subtle tint overlay, no border.
- **Window Border / Frame Stroke:** `#000000` at 8% opacity (`SurfaceStrokeColorDefault`).
- **Gutter Dividing Stroke:** `#000000` at 5% opacity.

### Syntax Spectrum (x86_64 / ARM)
- **Primary / Accent (`#60CDFF`):** Assembly Mnemonics / Opcodes (`mov`, `lea`, `xor`, `syscall`, `vmovdqu`, `stp`).
- **Registers (`#4CC2FF`):** CPU Registers (`rax`, `rsp`, `r11d`, `xmm0`, `x0`, `w1`).
- **Directives & Macros (`#AF00DB`):** Section and assembler control words (`global`, `extern`, `section .text`, `.align`, `equ`).
- **Immediate Values & Hex Numerics (`#098658`):** Offsets, pointers, and constants (`0x7FFF`, `42`, `$ - msg`).
- **Labels & Symbols (`#795E26`):** Function identifiers, branch targets, jump destinations (`_start`, `.loop_body`).
- **Memory Modifiers & Sizes (`#0070C1`):** Type indicators (`qword ptr`, `byte ptr`, `rel`).
- **Comments (`#008000`):** Explanatory notation starting with `;` or `//`.
- **Telemetry & Line Numbers (`#6E6E6E`):** Gutter digits, line counter, encoding indicators.

## Typography

The type system pairs Windows 11's core system font `Segoe UI Variable` (Text & Small variants) for OS-level affordances and ambient telemetry with a calibrated monospace engine (`Cascadia Code`) for precise column tracking in low-level code inspection.

- **Monospace Code Grid:** Rigid 22px line height (`body-code`) matching the gutter line-number increment 1:1, preventing vertical drift across millions of instruction lines.
- **Optical Legibility:** Zero font smoothing aberrations. Subpixel ClearType or DirectWrite grayscale rendering ensures sharp glyph execution on high-DPI displays.
- **Window Chrome:** The window title bar and status markers use `Segoe UI Variable` with strict tabular numeric alignment for coordinates.

## Layout & Spacing

The layout is monolithic and edge-to-edge, designed as a focused code viewport without conventional panels or palettes.

### Spatial Architecture
- **Title Bar Canvas:** 32px height at top edge, hosting the drag-safe region and native Windows 11 caption buttons (`Minimize`, `Maximize/Restore`, `Close`) flush against the top-right corner.
- **Assembly Viewport:** Fills 100% of remaining vertical and horizontal window dimensions.
- **Gutter Column:** Fixed left column spanning 52px width with a 16px right margin separating line integers from opcode columns.
- **Instruction Margin:** 16px left indentation from gutter divider to instruction start.
- **Status Telemetry Dock:** Non-intrusive floating indicator positioned at the bottom right (`space-md` [12px] from bottom edge, `space-lg` [16px] from right edge), rendered directly within the viewport buffer.

## Elevation & Depth

Visual hierarchy leverages Fluent Design Mica Alt materiality and low-contrast light strokes rather than exaggerated elevation shadows:

- **Desktop Host Layer:** Windows App SDK Mica Alt backing, drawing clean light into the window interior.
- **Window Boundary:** 1px uniform outer perimeter stroke (`rgba(0, 0, 0, 0.08)`) with zero outer drop shadow in maximized states, and subtle standard Windows DWM box shadow in floating windowed mode.
- **Active Cursor Row:** Rendered without shadow or elevation jump; uses a flat backdrop overlay (`#E5E5E5`) stretching across the full width of the editor canvas.
- **Telemetry Pill:** Layered on top of the editor layer with a subtle `Acrylic` brush (`#F3F3F3` at 70% opacity, 12px backdrop-filter blur) and a 1px boundary stroke (`rgba(0, 0, 0, 0.06)`).

## Shapes

The editor maintains WinUI 3's geometric architecture with measured, restrained corners:

- **Top-Level Window:** 8px outer border radius managed by the OS DWM (square when snapped or maximized).
- **Caption Control Buttons:** Sharp rectangular bounding boxes (0px) natively conforming to Windows 11 standard interaction hit-boxes (46px x 32px).
- **Telemetry Capsule:** Soft corners (4px, token level `1`) framing file statistics and cursor location.
- **Cursor Caret:** Crisp 0px rectangular line, 2px wide, colored `#60CDFF` with smooth WinUI blink animation.

## Components

### 1. Window Frame & Native Caption Controls
- **Height:** 32px standard caption bar integrated seamlessly into the editor canvas without an explicit border separator.
- **Caption Button Metrics:** 46px width x 32px height for minimize and maximize/restore; 47px width x 32px height for close.
- **Interactions:** Subtle hover fill (`#000000` at 6% opacity) for minimize/maximize, crimson fill (`#C42B1C`) on hover for close button with white glyph icon.

### 2. Code Buffer & Line Number Gutter
- **Line Numbers:** Right-aligned numerical labels rendered in `gutter-number` at `#6E6E6E`. The active row's number illuminates to `#000000` with medium weight.
- **Divider:** Continuous vertical 1px stroke at the right edge of the gutter with 5% black opacity.
- **Active Line Highlighting:** Full-width row rect filled with `#E5E5E5` tracking active caret vertical position smoothly.
- **Selection Highlight:** Solid `#ADD6FF` fill with rounded sub-selection edges.

### 3. Syntax Engine Renderers
- Distinctive high-performance lexical highlight mappings:
  - Instructions (`#60CDFF`): Standard assembly mnemonics.
  - Registers (`#4CC2FF`): Architecturally constrained processor registers.
  - Directives (`#AF00DB`): Assembler primitives.
  - Literals (`#098658`): Base-10 integers, binary representations (`0b...`), and hex tokens (`0x...`).
  - Comments (`#008000`): Italicized monospace or standard dim text.

### 4. Floating Ambient Status Capsule
- **Positioning:** Pinned 12px from bottom, 16px from right edge.
- **Composition:** Horizontal auto-layout with 8px internal item spacing, 4px vertical / 8px horizontal padding.
- **Content Nodes:**
  - Cursor location formatted as `Ln [row], Col [column]` using `status-value`.
  - Encoding standard: `UTF-8` or `ASCII`.
  - Target Architecture: `x86_64` or `AArch64`.
- **Visuals:** Acrylic backdrop tint, 1px border stroke, zero interference with code text underneath (interactive pointer-events passthrough when hovered).