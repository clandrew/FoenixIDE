﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Drawing.Text;
using System.Drawing.Imaging;
using FoenixIDE.Common;
using FoenixIDE.MemoryLocations;

namespace FoenixIDE.Display
{
    public partial class Gpu : UserControl, IMappable
    {
        public event KeyPressEventHandler KeyPressed;

        private const int REGISTER_BLOCK_SIZE = 256;
        const int MAX_TEXT_COLS = 128;
        const int MAX_TEXT_LINES = 64;
        const int SCREEN_PAGE_SIZE = 128 * 64;

        private int length = 128 * 64 * 2; //Text mode uses 16K, 1 page for text, the other for colors.

        public MemoryRAM VRAM = null;
        public MemoryRAM RAM = null;
        public MemoryRAM IO = null;

        public int StartAddress
        {
            get
            {
                return MemoryLocations.MemoryMap.SCREEN_PAGE0;
            }
        }

        public int Length
        {
            get
            {
                return length;
            }
        }

        public int EndAddress
        {
            get
            {
                return StartAddress + length - 1;
            }
        }

        //private int colorMatrixStart = 6096;
        //private int attributeStart = 8096;

        public List<CharacterSet> CharacterSetSlots = new List<CharacterSet>();

        /// <summary>
        /// number of frames to wait to refresh the screen.
        /// One frame = 1/60 second.
        /// </summary>
        public int RefreshTimer = 0;
        public int BlinkRate = 10;

        public ColorCodes CurrentColor = ColorCodes.White;

        // To provide a better contrast when writing on top of bitmaps
        Brush BackgroundTextBrush = new SolidBrush(Color.Black);
        Brush TextBrush = new SolidBrush(Color.LightBlue);
        Brush BorderBrush = new SolidBrush(Color.LightBlue);
        Brush InvertedBrush = new SolidBrush(Color.Blue);
        Brush CursorBrush = new SolidBrush(Color.LightBlue);

        static string MEASURE_STRING = new string('W', 80);

        Timer timer = new Timer();
        bool CursorEnabled = true;
        bool CursorState = true;

        /// <summary>
        /// Screen character data. Data is addressed as Data[i].
        /// </summary>
        //public char[] CharacterData = null;

        /// <summary>
        /// Screen color data. Upper nibble is background color. Lower nibble is foreground color. 
        /// </summary>
        //public ColorCodes[] ColorData = null;

        private int GetCharPos(int row, int col)
        {
            if (RAM == null)
                return 0;
            int baseAddress = RAM.ReadLong(MemoryMap.SCREENBEGIN);
            return baseAddress + row * COLS_PER_LINE + col;
        }

        /// <summary>
        /// Column of the cursor position. 0 is left edge
        /// </summary>
        [Browsable(false)]
        public int X
        {
            get
            {
                if (RAM == null)
                    return 0;

                return RAM.ReadByte(MemoryMap.CURSORX);
            }
            set
            {
                int x = value;
                if (x < 0)
                    x = 0;
                if (x >= ColumnsVisible)
                    x = ColumnsVisible - 1;
                if (RAM != null)
                    RAM.WriteByte(MemoryMap.CURSORX, (byte)x);
                ResetDrawTimer();
                CursorPos = GetCharPos(Y, x);
            }
        }

        /// <summary>
        /// Row of cursor position. 0 is top of the screen
        /// </summary>
        [Browsable(false)]
        public int Y
        {
            get
            {
                if (RAM == null)
                    return 0;

                return RAM.ReadByte(MemoryMap.CURSORY);
            }
            set
            {
                int y = value;
                if (y < 0)
                    y = 0;
                if (y >= LinesVisible)
                    y = LinesVisible - 1;
                if (RAM != null)
                    RAM.WriteByte(MemoryMap.CURSORY, (byte)y);
                ResetDrawTimer();
                CursorPos = GetCharPos(y, X);
            }
        }

        [Browsable(false)]
        public int ColumnsVisible
        {
            get
            {
                if (RAM == null)
                    return 0;

                return RAM.ReadByte(MemoryMap.COLS_VISIBLE);
            }
            set
            {
                if (RAM == null)
                    return;

                int i = value;
                if (i < 0)
                    i = 0;
                if (i > MAX_TEXT_COLS)
                    i = MAX_TEXT_COLS;
                RAM.WriteWord(MemoryMap.COLS_VISIBLE, i);
            }
        }

        [Browsable(false)]
        public int LinesVisible
        {
            get
            {
                if (RAM == null)
                    return 0;
                return RAM.ReadByte(MemoryMap.LINES_VISIBLE);
            }
            set
            {
                if (RAM == null)
                    return;

                int i = value;
                if (i < 0)
                    i = 0;
                if (i > MAX_TEXT_LINES)
                    i = MAX_TEXT_LINES;
                RAM.WriteWord(MemoryMap.LINES_VISIBLE, i);
            }
        }

        public int COLS_PER_LINE
        {
            get
            {
                if (RAM == null)
                    return 0;

                return RAM.ReadByte(MemoryMap.COLS_PER_LINE);
            }
            set
            {
                if (RAM == null)
                    return;

                int i = value;
                if (i < 0)
                    i = 0;
                if (i > MAX_TEXT_COLS)
                    i = MAX_TEXT_COLS;
                RAM.WriteWord(MemoryMap.COLS_PER_LINE, i);
            }
        }


        public Gpu()
        {
            InitializeComponent();

            this.Load += new EventHandler(Gpu_Load);
        }

        void Gpu_Load(object sender, EventArgs e)
        {

            this.SetScreenSize(25, 80);
            this.Paint += new PaintEventHandler(Gpu_Paint);
            timer.Tick += new EventHandler(timer_Tick);
            timer.Interval = 1000 / 60;
            this.VisibleChanged += new EventHandler(FrameBufferControl_VisibleChanged);
            this.DoubleBuffered = true;

            X = 0;
            Y = 0;

            if (DesignMode)
            {
                timer.Enabled = false;
            }
            else
            {
                if (ParentForm == null)
                    return;
                int htarget = 480;
                int topmargin = ParentForm.Height - ClientRectangle.Height;
                int sidemargin = ParentForm.Width - ClientRectangle.Width;
                ParentForm.Height = htarget + topmargin;
                ParentForm.Width = (int)Math.Ceiling(htarget * 1.6) + sidemargin;
            }
        }

        public void LoadCharacterData()
        {
            LoadFontSet("ASCII-PET", @"Resources\FOENIX-CHARACTER-ASCII.bin", 0, CharacterSet.CharTypeCodes.ASCII_PET, CharacterSet.SizeCodes.Size8x8);
            //LoadCharacterSet("ASCII-PET", @"Resources\FOENIX-CHARACTER-ASCII.bin", 0, CharacterSet.CharTypeCodes.ASCII_PET, CharacterSet.SizeCodes.Size8x8);
            //LoadCharacterSet("PETSCII_GRAPHICS", @"Resources\PETSCII.901225-01.bin", 0, CharacterSet.CharTypeCodes.PETSCII_GRAPHICS, CharacterSet.SizeCodes.Size8x8);
            //LoadCharacterSet("PETSCII_TEXT", @"Resources\PETSCII.901225-01.bin", 4096, CharacterSet.CharTypeCodes.PETSCII_TEXT, CharacterSet.SizeCodes.Size8x8);
        }

        private Font GetBestFont()
        {
            Font useFont = null;
            float rowHeight = this.ClientRectangle.Height / (float)LinesVisible;
            if (rowHeight < 8)
                rowHeight = 8;

            var fonts = new[]
            {
                "C64 Pro Mono",
                "Consolas",
                //"Classic Console",
                //"Glass TTY VT220",
                "Lucida Console",
            };

#if DEBUGx
            InstalledFontCollection installedFontCollection = new InstalledFontCollection();

            // Get the array of FontFamily objects.
            var fontFamilies = installedFontCollection.Families;

            // The loop below creates a large string that is a comma-separated
            // list of all font family names.

            int count = fontFamilies.Length;
            for (int j = 0; j < count; ++j)
            {
                System.Diagnostics.Debug.WriteLine("Font: " + fontFamilies[j].Name);
            }
#endif

            foreach (var f in fonts)
            {
                using (Font fontTester = new Font(
                        f,
                        rowHeight,
                        FontStyle.Regular,
                        GraphicsUnit.Pixel))
                {
                    if (fontTester.Name == f)
                    {
                        useFont = new Font(f, rowHeight, FontStyle.Regular, GraphicsUnit.Pixel);
                        break;
                    }
                    else
                    {
                    }
                }
            }
            if (useFont == null)
                useFont = new Font(this.Font, FontStyle.Regular);

            Graphics g = this.CreateGraphics();
            SizeF fs = MeasureFont(useFont, g);
            float ratio = rowHeight / fs.Height;
            float newSize = rowHeight * ratio;
            useFont = new Font(useFont.FontFamily, newSize, FontStyle.Regular, GraphicsUnit.Pixel);

            return useFont;
        }

        public void ResetDrawTimer()
        {
            RefreshTimer = 0;
            CursorState = true;
        }


        public int BufferSize
        {
            get
            {
                return MAX_TEXT_COLS * MAX_TEXT_LINES;
            }
        }

        /// <summary>
        /// Memory offset of the cursor position on the screen. The top-left corner is the first memory location
        /// of the screen. 
        /// </summary>
        [Browsable(false)]
        public int CursorPos
        {
            get
            {
                if (RAM == null)
                    return 0;
                return RAM.ReadWord(MemoryMap.CURSORPOS);
            }

            set
            {
                if (RAM == null)
                    return;
                RAM.WriteWord(MemoryMap.CURSORPOS, value);
            }
        }

        /// <summary>
        /// Draw the frame buffer to the screen.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void Gpu_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            //g.CompositingQuality = global::System.Drawing.Drawing2D.CompositingQuality.HighSpeed;
            //g.InterpolationMode = global::System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;

            Bitmap frameBuffer = new Bitmap(640, 480, PixelFormat.Format32bppArgb);

            if (VRAM == null)
            {
                g.DrawString("VRAM Not initialized", this.Font, TextBrush, 0, 0);
                return;
            }
            if (RAM == null)
            {
                g.DrawString("CodeRAM Not initialized", this.Font, TextBrush, 0, 0);
                return;
            }

            // Text Mode
            byte MCRegister = IO.ReadByte(0); // Reading address $AF:0000
            // Bitmap Mode
            if ((MCRegister & 0x8) == 0x8)
            {
                DrawBitmap(frameBuffer);
            }
            if ((MCRegister & 0x1) == 0x1)
            {
                int top = 0;
                if (ColumnsVisible < 1 || ColumnsVisible > 128)
                {
                    Graphics graphics = Graphics.FromImage(frameBuffer);
                    DrawTextWithBackground("ColumnsVisible invalid:" + ColumnsVisible.ToString(), graphics, Color.Black, 0, top);
                    top += 12;
                }
                if (LinesVisible < 1)
                {
                    Graphics graphics = Graphics.FromImage(frameBuffer);
                    DrawTextWithBackground("LinesVisible invalid:" + LinesVisible.ToString(), graphics, Color.Black, 0, top);
                    top += 12;
                }
                if (top == 0)
                {
                    DrawBitmapText(frameBuffer);
                }
            }
            // Overlay Mode
            if ((MCRegister & 0x2) == 0x2)
            {

            }
            // Graphics Mode
            if ((MCRegister & 0x4) == 0x4)
            {

            }
            g.DrawImage(frameBuffer, 0, 0, this.ClientRectangle.Width, this.ClientRectangle.Height);

        }

        /*
         * Display the text with a colored background. This should make the text more visible against bitmaps.
         */
        private void DrawTextWithBackground(String text, Graphics g, Color backgroundColor, int x, int y)
        {
            g.DrawString(text, this.Font, BackgroundTextBrush, x, y);
            g.DrawString(text, this.Font, BackgroundTextBrush, x + 2, y);
            g.DrawString(text, this.Font, BackgroundTextBrush, x, y + 2);
            g.DrawString(text, this.Font, BackgroundTextBrush, x + 2, y + 2);
            g.DrawString(text, this.Font, TextBrush, x + 1, y + 1);
        }
        int lastWidth = 0;
        private void DrawBitmapText(Bitmap bitmap)
        {
            if (lastWidth != ColumnsVisible
                && ColumnsVisible > 0
                && LinesVisible > 0)
            {
                lastWidth = ColumnsVisible;
            }
            bool overlayBitSet = (IO.ReadByte(0) & 0x02) == 0x02;

            float x;
            float y;

            Graphics g = Graphics.FromImage(bitmap);
            // Read the color lookup tables
            Color[] fgColorLUT = new Color[16];
            Color[] bgColorLUT = new Color[16];
            int fgLUT = MemoryLocations.MemoryMap.FG_CHAR_LUT_PTR - IO.StartAddress;
            int bgLUT = MemoryLocations.MemoryMap.BG_CHAR_LUT_PTR - IO.StartAddress;
            for (int c = 0; c < 16; c++)
            {
                // Foreground
                byte blue = IO.ReadByte(fgLUT++);
                byte green = IO.ReadByte(fgLUT++);
                byte red = IO.ReadByte(fgLUT++);
                fgLUT++;
                fgColorLUT[c] = Color.FromArgb(red, green, blue);

                // Background
                blue = IO.ReadByte(bgLUT++);
                green = IO.ReadByte(bgLUT++);
                red = IO.ReadByte(bgLUT++);
                bgLUT++;
                bgColorLUT[c] = Color.FromArgb(red, green, blue);
            }

            int charWidth = 8;
            int charHeight = 8;

            int col = 0, line = 0;

            int colorStart = MemoryLocations.MemoryMap.SCREEN_PAGE1 - IO.StartAddress;
            int colOffset = (80 - ColumnsVisible) / 2 * charWidth;
            int lineStart = MemoryLocations.MemoryMap.SCREEN_PAGE0 - IO.StartAddress;
            int lineOffset = (60 - LinesVisible) / 2 * charHeight;
            
            for (line = 0; line < LinesVisible; line++)
            {
                int textAddr = lineStart;
                int colorAddr = colorStart;
                for (col = 0; col < ColumnsVisible; col++)
                {
                    x = col * charWidth;
                    y = line * charHeight;
                    byte character = IO.ReadByte(textAddr++);

                    byte color = IO.ReadByte(colorAddr++);
                    byte fgColor = (byte)((color & 0xF0) >> 4);
                    byte bgColor = (byte)(color & 0x0F);

                    Bitmap bmp = CharacterSetSlots[0].Bitmaps[character];
                        
                    ColorPalette pal = bmp.Palette;
                    if (!overlayBitSet && (bgColor != 0))
                    {
                        pal.Entries[0] = bgColorLUT[bgColor];
                    }
                    else
                    {
                        pal.Entries[0] = Color.Transparent;
                    }
                    if (fgColor != 0)
                    {
                        pal.Entries[1] = fgColorLUT[fgColor];
                    }
                    bmp.Palette = pal;

                    RectangleF rect = new RectangleF((int)x + colOffset, (int)y + lineOffset, bmp.Width, bmp.Height);
                    
                    g.DrawImage(bmp, rect);
                    
                }
                lineStart += COLS_PER_LINE;
                colorStart += COLS_PER_LINE;
            }

            if (CursorState && CursorEnabled)
            {
                x = X * charWidth;
                y = Y * charHeight;
                g.FillRectangle(CursorBrush, x + colOffset, y + lineOffset, charWidth, charHeight);
                //g.DrawString(CharacterData[GetCharPos(Y, X)].ToString(),
                //    TextFont,
                //    InvertedBrush,
                //    x, y,
                //    StringFormat.GenericTypographic);
            }
        }

        private void DrawBitmap(Bitmap bitmap)
        {
            // Bitmap Controller is located at $AF:0140
            int reg = IO.ReadByte(0xAF_0140 - 0xAF_0000);
            if ((reg & 0x01) == 00)
            {
                return;
            }
            int LUT = (reg & 14) >> 1;  // 8 possible LUTs

            int bitmapAddress = IO.ReadLong(0xAF_0141 - 0xAF_0000);
            int width = IO.ReadWord(0xAF_0144 - 0xAF_0000);
            int height = IO.ReadWord(0xAF_0146 - 0xAF_0000);

            //Bitmap frameBuffer = new Bitmap(width, height, PixelFormat.Format8bppIndexed);
            // Read the color lookup tables
            int lutAddress = LUT * 1024 + 0xAF_2000 - 0xAF_0000;
            //ColorPalette pal = frameBuffer.Palette;
            int[] lut = new int[256];
            for (int c = 0; c < 256; c++)
            {
                // Foreground
                byte blue = IO.ReadByte(lutAddress++);
                byte green = IO.ReadByte(lutAddress++);
                byte red = IO.ReadByte(lutAddress++);
                lutAddress++;
                lut[c] = (255 << 24) + (red << 16) + (green << 8) + blue;
                //pal.Entries[c] = Color.FromArgb(red, green, blue);
            }
            //frameBuffer.Palette = pal;

            int col = 0, line = 0;
            BitmapData bitmapData = bitmap.LockBits(new Rectangle(0,0,640, 480), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            IntPtr p = bitmapData.Scan0;
            for (line = 0; line < height; line++)
            {
                for (col = 0; col < width; col++)
                {
                    int value = (int) lut[VRAM.ReadByte(bitmapAddress++)];
                    System.Runtime.InteropServices.Marshal.WriteInt32(p, (line * bitmap.Width + col) * 4, value);
                    //System.Runtime.InteropServices.Marshal.WriteByte(p, line * bitmapData.Width + col, ));
                }
            }
            bitmap.UnlockBits(bitmapData);
        }

        private SizeF MeasureFont(Font font, Graphics g)
        {
            return g.MeasureString(MEASURE_STRING, font, int.MaxValue, StringFormat.GenericTypographic);
        }

        void timer_Tick(object sender, EventArgs e)
        {
            if (RefreshTimer-- > 0)
            {
                if (Form.ActiveForm == this.ParentForm)
                    Refresh();
                return;
            }

            this.Refresh();
            CursorState = !CursorState;
            RefreshTimer = BlinkRate;
        }

        void FrameBufferControl_VisibleChanged(object sender, EventArgs e)
        {
            timer.Enabled = this.Visible;
        }

        private void FrameBuffer_SizeChanged(object sender, global::System.EventArgs e)
        {
            //TextFont = GetBestFont();
        }

        private void FrameBuffer_KeyPress(object sender, KeyPressEventArgs e)
        {
            TerminalKeyEventArgs args = new TerminalKeyEventArgs(e.KeyChar);
            KeyPressed?.Invoke(this, args);
        }

        public byte ReadByte()
        {
            return 0;
        }

        public virtual void SetScreenSize(int Lines, int Columns)
        {
            this.ColumnsVisible = Columns;
            this.LinesVisible = Lines;
        }

        public byte ReadByte(int Address)
        {
            if (VRAM == null)
                return 0;
            return VRAM.ReadByte(Address - VRAM.StartAddress);
        }

        /// return the GPU registers: start of text page, start of color page, start of character data, 
        /// number of columns, number of LINES, graphics mode, etc. 
        /// </summary>
        /// <param name="Address">Address to read</param>
        /// <returns></returns>
        public byte ReadGPURegister(int Address)
        {
            return 0;
        }

        public void WriteGPURegister(int Address, byte Data)
        {

        }

        public void WriteByte(int Address, byte Data)
        {
            if (IO == null)
                return;
            IO.WriteByte(Address - VRAM.StartAddress, Data);
            //else if (Address >= characterMatrixStart && Address < (characterMatrixStart + CharacterData.Length))
            //{
            //    CharacterData[Address - characterMatrixStart] = (char)Data;
            //}
            //else if (Address >= colorMatrixStart && Address < (colorMatrixStart + ColorData.Length))
            //{
            //    ColorData[Address - colorMatrixStart] = (ColorCodes)Data;
            //}
        }

        /// <summary>
        /// Loads a font set into RAM and adds it to the character set table.
        /// </summary>
        /// <param name="Address"></param>
        /// <param name="Filename"></param>
        public CharacterSet LoadFontSet(string Name, string Filename, int Offset, CharacterSet.CharTypeCodes CharType, CharacterSet.SizeCodes CharSize)
        {
            CharacterSet cs = new CharacterSet();
            // Load the data from the file into the  IO buffer - starting at address $AF8000
            cs.Load(Filename, Offset, IO, MemoryLocations.MemoryMap.FONT_MEMORY_BANK_START & 0xffff, CharSize);
            
            CharacterSetSlots.Add(cs);
            cs.CharType = CharType;
            return cs;
        }

        protected override bool IsInputKey(Keys keyData)
        {
            return true;
        }
    }
}
