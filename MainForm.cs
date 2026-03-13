#nullable disable
#pragma warning disable CS8618, CS8600, CS8603

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MKVCleaner
{
    public partial class MainForm : Form
    {
        // ══════════════════════════════════════════════════════════════════
        //  PALETA — Cabecera oscura + contenido claro + acentos verdes
        // ══════════════════════════════════════════════════════════════════

        // Cabecera oscura
        private static readonly Color CHeaderBg    = Color.FromArgb(18, 18, 18);
        private static readonly Color CHeaderText  = Color.FromArgb(240, 240, 240);
        private static readonly Color CHeaderSub   = Color.FromArgb(140, 140, 140);
        private static readonly Color CHeaderBorde = Color.FromArgb(0, 160, 70);   // linea verde bajo header

        // Contenido claro
        private static readonly Color CFondo       = Color.FromArgb(245, 247, 245);
        private static readonly Color CPanelBg     = Color.White;
        private static readonly Color CPanelBorde  = Color.FromArgb(218, 230, 220);
        private static readonly Color CTexto       = Color.FromArgb(30, 40, 30);
        private static readonly Color CTextoSub    = Color.FromArgb(100, 115, 100);

        // Acentos verdes
        private static readonly Color CVerde       = Color.FromArgb(0, 160, 70);
        private static readonly Color CVerdeOsc    = Color.FromArgb(0, 120, 52);
        private static readonly Color CVerdeClar   = Color.FromArgb(230, 247, 235);
        private static readonly Color CVerdeText   = Color.White;

        // Barra inferior oscura
        private static readonly Color CBarraBg     = Color.FromArgb(22, 22, 22);
        private static readonly Color CBarraText   = Color.FromArgb(200, 200, 200);

        // Botones secundarios
        private static readonly Color CBtnSecBg    = Color.FromArgb(235, 240, 236);
        private static readonly Color CBtnSecBorde = Color.FromArgb(190, 210, 195);
        private static readonly Color CBtnSecText  = Color.FromArgb(30, 80, 45);

        // Configuracion persistente
        private static readonly string RutaConfig = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AndalusianMKVCleaner", "config.json");

        // ── Estado ────────────────────────────────────────────────────────
        private List<MkvFile>           _archivos     = new List<MkvFile>();
        private string                  _carpetaDest  = string.Empty;
        private CancellationTokenSource _cts          = null;
        private bool                    _actualizando = false;
        private string                  _mkvmergePath = null;

        // ── Controles ─────────────────────────────────────────────────────
        private Panel                pnlHeader;
        private Panel                pnlToolbar;
        private Button               btnAgregarFich;
        private Button               btnAgregarCarp;
        private Button               btnEscanearCarp;
        private Button               btnLimpiar;
        private Button               btnGuardarPerfil;
        private Button               btnCargarPerfil;
        private SplitContainer       splitMain;
        private ListView             lvArchivos;
        private Panel                pnlPistas;
        private CheckedListBox       clbPistas;
        private Panel                pnlBottom;
        private ProgressBar          pbGeneral;
        private Label                lblArchivoActual;
        private Label                lblProgreso;
        private Label                lblContadores;
        private Button               btnMultiplexar;
        private Button               btnDetener;
        private Button               btnDestino;
        private Panel                pnlStatus;
        private Label                lblStatus;

        // ══════════════════════════════════════════════════════════════════
        //  CONSTRUCTOR
        // ══════════════════════════════════════════════════════════════════
        public MainForm()
        {
            InitializeComponent();
            ConfigurarVentana();
            ConstruirUI();
            CargarConfiguracion();
            this.AllowDrop  = true;
            this.DragEnter += OnDragEnter;
            this.DragDrop  += OnDragDrop;
            this.Shown      += MainForm_Shown;
        }

        // ══════════════════════════════════════════════════════════════════
        //  CONFIGURACION PERSISTENTE
        // ══════════════════════════════════════════════════════════════════
        private void CargarConfiguracion()
        {
            try {
                if (!File.Exists(RutaConfig)) return;
                var cfg = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(RutaConfig));
                if (cfg.TryGetValue("mkvmergePath", out string ruta) && File.Exists(ruta))
                    _mkvmergePath = ruta;
            } catch { }
        }

        private void GuardarConfiguracion()
        {
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(RutaConfig));
                var cfg = new Dictionary<string, string>();
                if (!string.IsNullOrEmpty(_mkvmergePath)) cfg["mkvmergePath"] = _mkvmergePath;
                File.WriteAllText(RutaConfig,
                    JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true }));
            } catch { }
        }

        // ══════════════════════════════════════════════════════════════════
        //  INICIO
        // ══════════════════════════════════════════════════════════════════
        private void MainForm_Shown(object sender, EventArgs e)
        {
            // Dividir el SplitContainer por la mitad exacta una vez la ventana tiene su tamaño real
            splitMain.SplitterDistance = splitMain.Height / 2;

            string mk = EncontrarMkvmerge();
            if (mk == null) {
                SetStatus("mkvmerge no encontrado — usa el boton Examinar para localizarlo", true);
                MostrarDialogoMkvmerge();
            } else {
                SetStatus("Listo  |  mkvmerge: " + mk, false);
            }
        }

        private void MostrarDialogoMkvmerge()
        {
            int mL = 28, w = 504;
            var dlg = new Form {
                Text = "mkvmerge no encontrado", Size = new Size(560, 400),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false, MinimizeBox = false,
                BackColor = Color.White, Font = new Font("Segoe UI", 9f)
            };
            var picIcon = new PictureBox {
                Image = SystemIcons.Warning.ToBitmap(), Size = new Size(40, 40),
                Location = new Point(mL, 18), SizeMode = PictureBoxSizeMode.StretchImage
            };
            var lblTitulo = new Label {
                Text = "No se ha encontrado mkvmerge.exe",
                ForeColor = Color.FromArgb(180, 20, 20),
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                AutoSize = false, Size = new Size(430, 28),
                Location = new Point(mL + 48, 24)
            };
            var lblMensaje = new Label {
                Text =
                    "Este programa necesita MKVToolNix para funcionar.\n\n" +
                    "Opciones:\n\n" +
                    "  1.  Instala MKVToolNix (recomendado):\n" +
                    "       https://mkvtoolnix.download/\n" +
                    "       (se instala en C:\\Program Files\\MKVToolNix\\)\n\n" +
                    "  2.  Copia mkvmerge.exe junto a este programa (.exe).\n\n" +
                    "  3.  Indica manualmente la carpeta de instalacion de MKVToolNix:",
                AutoSize = false, Size = new Size(w, 185),
                Location = new Point(mL, 65), ForeColor = Color.FromArgb(40, 40, 40)
            };
            var txtRuta = new TextBox {
                Size = new Size(w - 120, 24), Location = new Point(mL, 258),
                PlaceholderText = "Ruta a la carpeta de MKVToolNix...",
                Font = new Font("Segoe UI", 8.5f)
            };
            var btnExaminar = new Button {
                Text = "Examinar...", Size = new Size(108, 26),
                Location = new Point(mL + w - 108, 257),
                FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 95, 170),
                ForeColor = Color.White, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Cursor = Cursors.Hand, UseVisualStyleBackColor = false
            };
            btnExaminar.FlatAppearance.BorderSize = 1;
            btnExaminar.Click += (s, ev) => {
                using var fd = new FolderBrowserDialog { Description = "Carpeta de MKVToolNix" };
                if (fd.ShowDialog() == DialogResult.OK) txtRuta.Text = fd.SelectedPath;
            };
            var btnOK = new Button {
                Text = "Aceptar", Size = new Size(100, 30),
                Location = new Point((560 - 100) / 2, 308),
                FlatStyle = FlatStyle.Flat, BackColor = CVerde,
                ForeColor = Color.White, Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                DialogResult = DialogResult.OK, Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false
            };
            btnOK.FlatAppearance.BorderSize = 1;
            dlg.AcceptButton = btnOK;
            dlg.Controls.AddRange(new Control[] { picIcon, lblTitulo, lblMensaje, txtRuta, btnExaminar, btnOK });
            dlg.ShowDialog(this);

            if (!string.IsNullOrWhiteSpace(txtRuta.Text)) {
                string candidato = Path.Combine(txtRuta.Text.Trim(), "mkvmerge.exe");
                if (File.Exists(candidato)) {
                    _mkvmergePath = candidato;
                    GuardarConfiguracion();
                    SetStatus("Listo  |  mkvmerge: " + candidato, false);
                } else {
                    MessageBox.Show("No se encontro mkvmerge.exe en:\n" + txtRuta.Text,
                        "Ruta no valida", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  VENTANA Y UI
        // ══════════════════════════════════════════════════════════════════
        private void ConfigurarVentana()
        {
            this.Text          = "Andalusian MKV Cleaner";
            this.Size          = new Size(1060, 780);
            this.MinimumSize   = new Size(860, 660);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.WindowState    = FormWindowState.Maximized;
            this.BackColor     = CFondo;
            this.Font          = new Font("Segoe UI", 9f);
        }

        private void ConstruirUI()
        {
            // ── Icono de ventana ──────────────────────────────────────────
            try {
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                string ico = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("AMKVC.ico"));
                if (ico != null) { using var st = asm.GetManifestResourceStream(ico); this.Icon = new Icon(st); }
            } catch { }

            // ── CABECERA OSCURA ───────────────────────────────────────────
            pnlHeader = new Panel {
                Dock = DockStyle.Top, Height = 56, BackColor = CHeaderBg
            };
            pnlHeader.Paint += DibujarCabecera;

            // ── TOOLBAR (claro con botones redondeados) ───────────────────
            pnlToolbar = new Panel {
                Dock = DockStyle.Top, Height = 52, BackColor = CFondo
            };
            pnlToolbar.Paint += (s, ev) => {
                ev.Graphics.DrawLine(new Pen(CPanelBorde, 1),
                    0, pnlToolbar.Height - 1, pnlToolbar.Width, pnlToolbar.Height - 1);
            };

            btnAgregarFich  = CrearBotonVerde("+ Agregar MKV", 140);
            btnAgregarFich.Location = new Point(10, 11);
            btnAgregarFich.Click   += BtnAgregarFich_Click;

            btnAgregarCarp  = CrearBotonVerde("+ Carpeta", 105);
            btnAgregarCarp.Location = new Point(158, 11);
            btnAgregarCarp.Click   += BtnAgregarCarp_Click;

            btnEscanearCarp = CrearBotonVerde("Escanear carpeta", 138);
            btnEscanearCarp.Location = new Point(271, 11);
            btnEscanearCarp.Click   += BtnEscanearCarp_Click;

            // Separador visual
            var sep1 = new Panel { Size = new Size(1, 26), Location = new Point(418, 13), BackColor = CPanelBorde };

            btnGuardarPerfil = CrearBotonSecundario("Guardar perfil", 118);
            btnGuardarPerfil.Location = new Point(428, 11);
            btnGuardarPerfil.Click   += BtnGuardarPerfil_Click;

            btnCargarPerfil  = CrearBotonSecundario("Cargar perfil", 112);
            btnCargarPerfil.Location = new Point(554, 11);
            btnCargarPerfil.Click   += BtnCargarPerfil_Click;

            var sep2 = new Panel { Size = new Size(1, 26), Location = new Point(674, 13), BackColor = CPanelBorde };

            btnLimpiar = CrearBotonPeligro("Limpiar lista", 112);
            btnLimpiar.Location = new Point(684, 11);
            btnLimpiar.Click   += BtnLimpiar_Click;

            pnlToolbar.Controls.AddRange(new Control[] {
                btnAgregarFich, btnAgregarCarp, btnEscanearCarp, sep1,
                btnGuardarPerfil, btnCargarPerfil, sep2, btnLimpiar
            });

            // ── ZONA CENTRAL ──────────────────────────────────────────────
            splitMain = new SplitContainer {
                Dock = DockStyle.Fill, Orientation = Orientation.Horizontal,
                Panel1MinSize = 120, Panel2MinSize = 120,
                BackColor = CFondo, SplitterWidth = 8
            };
            splitMain.Paint += (s, ev) => {
                // Linea separadora sutil
                int y = splitMain.SplitterRectangle.Y + splitMain.SplitterRectangle.Height / 2;
                ev.Graphics.DrawLine(new Pen(CPanelBorde, 1), 0, y, splitMain.Width, y);
            };

            // Panel superior: lista archivos — GroupBox estilo clasico
            splitMain.Panel1.Padding = new Padding(8, 6, 8, 2);
            splitMain.Panel1.BackColor = CFondo;

            var grpArchivos = new GroupBox {
                Text      = "  Archivos MKV  (arrastra ficheros o carpetas aqui)",
                ForeColor = CVerdeOsc,
                Font      = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Dock      = DockStyle.Fill,
                Padding   = new Padding(6)
            };

            lvArchivos = new ListView {
                View = View.Details, FullRowSelect = true, GridLines = true,
                BackColor = Color.White, ForeColor = CTexto,
                Font = new Font("Segoe UI", 8.5f),
                BorderStyle = BorderStyle.FixedSingle,
                CheckBoxes = true, Dock = DockStyle.Fill,
                AllowDrop = true
            };
            // Activar doble buffer para eliminar parpadeo al actualizar filas
            typeof(ListView).GetProperty("DoubleBuffered",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(lvArchivos, true);
            lvArchivos.Columns.Add("Archivo MKV",     600);
            lvArchivos.Columns.Add("Original",        105);
            lvArchivos.Columns.Add("Estimado",        110);
            lvArchivos.Columns.Add("Ahorro",           70);
            lvArchivos.Columns.Add("Estado",          190);
            lvArchivos.DragEnter   += OnDragEnter;
            lvArchivos.DragDrop    += OnDragDrop;
            lvArchivos.ItemChecked += LvArchivos_ItemChecked;
            lvArchivos.KeyDown     += LvArchivos_KeyDown;

            var ctxMenu = new ContextMenuStrip();
            var mnuEliminar = new ToolStripMenuItem("Eliminar de la lista");
            mnuEliminar.Click += (s, ev) => EliminarSeleccionados();
            ctxMenu.Items.Add(mnuEliminar);
            lvArchivos.ContextMenuStrip = ctxMenu;

            grpArchivos.Controls.Add(lvArchivos);
            splitMain.Panel1.Controls.Add(grpArchivos);

            // Panel inferior: pistas — GroupBox estilo clasico
            splitMain.Panel2.Padding = new Padding(8, 2, 8, 8);
            splitMain.Panel2.BackColor = CFondo;

            pnlPistas = new Panel { Dock = DockStyle.Fill };  // placeholder para campo

            var grpPistas2 = new GroupBox {
                Text      = "  MARCADOS = conservar  —  DESMARCADOS = eliminar",
                ForeColor = CVerdeOsc,
                Font      = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Dock      = DockStyle.Fill,
                Padding   = new Padding(6)
            };

            clbPistas = new CheckedListBox {
                BackColor = Color.White, ForeColor = CTexto,
                Font = new Font("Segoe UI", 9.5f),
                CheckOnClick = true,
                BorderStyle = BorderStyle.FixedSingle,
                Dock = DockStyle.Fill
            };
            clbPistas.ItemCheck += ClbPistas_ItemCheck;

            grpPistas2.Controls.Add(clbPistas);
            splitMain.Panel2.Controls.Add(grpPistas2);

            // ── BARRA INFERIOR OSCURA ─────────────────────────────────────
            pnlBottom = new Panel {
                Dock = DockStyle.Bottom, Height = 90, BackColor = CBarraBg
            };
            pnlBottom.Resize += (s, ev) => ReposicionarBotonesBottom();

            // Carpeta destino (izquierda)
            btnDestino = new Button {
                Text = "Carpeta destino...",
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.FromArgb(200, 200, 200),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(148, 32),
                Location = new Point(14, 22),
                Font = new Font("Segoe UI", 8.5f),
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false
            };
            btnDestino.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 70);
            btnDestino.FlatAppearance.BorderSize  = 1;
            btnDestino.Click += BtnDestino_Click;

            // Estado (centro-izquierda)
            lblArchivoActual = new Label {
                Text = "Listo para procesar.",
                ForeColor = Color.FromArgb(225, 225, 225), AutoSize = false,
                Size = new Size(400, 18), Location = new Point(172, 20),
                Font = new Font("Segoe UI", 9.5f),
                Anchor = AnchorStyles.Left | AnchorStyles.Top
            };

            // Barra de progreso (centro)
            pbGeneral = new ProgressBar {
                Location = new Point(172, 44), Size = new Size(400, 6),
                Minimum = 0, Maximum = 100, Value = 0,
                Style = ProgressBarStyle.Continuous,
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top
            };

            lblProgreso = new Label {
                Text = "", ForeColor = Color.FromArgb(170, 170, 170),
                AutoSize = false, Size = new Size(400, 15),
                Location = new Point(172, 56),
                Font = new Font("Segoe UI", 8.5f),
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top
            };

            // Contador archivos/tamaño (derecha del estado)
            lblContadores = new Label {
                Text = "0 archivos",
                ForeColor = Color.FromArgb(170, 170, 170),
                AutoSize = true,
                Location = new Point(14, 60),
                Font = new Font("Segoe UI", 8.5f)
            };

            // Botones accion (derecha)
            btnMultiplexar = new Button {
                Text = "  Extraer / Limpiar",
                BackColor = CVerde, ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(158, 50),
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false,
                Anchor = AnchorStyles.Right | AnchorStyles.Top,
                TextAlign = ContentAlignment.MiddleCenter
            };
            btnMultiplexar.FlatAppearance.BorderColor = CVerdeOsc;
            btnMultiplexar.FlatAppearance.BorderSize  = 1;
            btnMultiplexar.Click += BtnMultiplexar_Click;

            btnDetener = new Button {
                Text = "Detener",
                BackColor = Color.FromArgb(180, 45, 45), ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(90, 50),
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false,
                Enabled = false,
                Anchor = AnchorStyles.Right | AnchorStyles.Top
            };
            btnDetener.FlatAppearance.BorderColor = Color.FromArgb(140, 30, 30);
            btnDetener.FlatAppearance.BorderSize  = 1;
            btnDetener.Click += BtnDetener_Click;

            pnlBottom.Controls.AddRange(new Control[] {
                btnDestino, lblArchivoActual, pbGeneral, lblProgreso,
                lblContadores, btnMultiplexar, btnDetener
            });

            // ── LINEA DE STATUS (muy fina, encima de la barra bottom) ─────
            pnlStatus = new Panel {
                Dock = DockStyle.Bottom, Height = 26,
                BackColor = Color.FromArgb(30, 30, 30)
            };
            lblStatus = new Label {
                Text = "Andalusian MKV Cleaner  |  Carga archivos para comenzar",
                ForeColor = Color.FromArgb(185, 185, 185),
                Font = new Font("Segoe UI", 8.5f),
                AutoSize = false, Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(10, 0, 0, 0)
            };
            pnlStatus.Controls.Add(lblStatus);

            // ── ENSAMBLADO ────────────────────────────────────────────────
            this.Controls.Add(splitMain);
            this.Controls.Add(pnlStatus);
            this.Controls.Add(pnlBottom);
            this.Controls.Add(pnlToolbar);
            this.Controls.Add(pnlHeader);
            ReposicionarBotonesBottom();
        }

        private void ReposicionarBotonesBottom()
        {
            if (btnMultiplexar == null || pnlBottom == null) return;
            int y = (pnlBottom.Height - btnMultiplexar.Height) / 2;
            btnDetener.Location     = new Point(pnlBottom.Width - 14 - btnDetener.Width, y);
            btnMultiplexar.Location = new Point(btnDetener.Left - 8 - btnMultiplexar.Width, y);
            // Ajustar ancho de progreso para no solapar botones
            int rightEdge = btnMultiplexar.Left - 20;
            pbGeneral.Width        = rightEdge - pbGeneral.Left;
            lblArchivoActual.Width = rightEdge - lblArchivoActual.Left;
            lblProgreso.Width      = rightEdge - lblProgreso.Left;
        }

        // ══════════════════════════════════════════════════════════════════
        //  DIBUJO: CABECERA OSCURA
        // ══════════════════════════════════════════════════════════════════
        private void DibujarCabecera(object sender, PaintEventArgs e)
        {
            var g  = e.Graphics;
            var rc = pnlHeader.ClientRectangle;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Fondo oscuro solido
            g.FillRectangle(new SolidBrush(CHeaderBg), rc);

            // Linea verde brillante en la parte inferior
            using var pVerde = new Pen(CHeaderBorde, 2);
            g.DrawLine(pVerde, 0, rc.Height - 1, rc.Width, rc.Height - 1);

            // Logo
            DibujarLogo(g, 28, (rc.Height - 36) / 5, 46);

            // Titulo
            int tx = 80;
            using var fT  = new Font("Segoe UI", 15f, FontStyle.Bold);
            using var brT = new SolidBrush(CHeaderText);
            g.DrawString("Andalusian MKV Cleaner", fT, brT, tx, 10);

            // Subtitulo
            using var fS  = new Font("Segoe UI", 8f);
            using var brS = new SolidBrush(CHeaderSub);
            g.DrawString("Limpiador de pistas MKV por lotes", fS, brS, tx + 2, 38);

            // Bloque de creditos alineado a la derecha
            using var fV1  = new Font("Segoe UI", 8f, FontStyle.Bold);
            using var fV2  = new Font("Segoe UI", 7.5f);
            using var brV1 = new SolidBrush(Color.FromArgb(190, 190, 190));
            using var brV2 = new SolidBrush(Color.FromArgb(110, 110, 110));
            string linea1 = "AMKVC  ·  Created by AlfTB";
            string linea2 = "co-created with Claude";
            var sz1 = g.MeasureString(linea1, fV1);
            var sz2 = g.MeasureString(linea2, fV2);
            float rx = rc.Width - Math.Max(sz1.Width, sz2.Width) - 16;
            g.DrawString(linea1, fV1, brV1, rx, 12);
            g.DrawString(linea2, fV2, brV2, rx, 12 + sz1.Height + 1);
        }

        private void DibujarLogo(Graphics g, int x, int y, int size)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = size;
            var path = new GraphicsPath();
            float r = s * 0.22f;
            path.AddArc(x,           y,           r*2, r*2, 180, 90);
            path.AddArc(x + s - r*2, y,           r*2, r*2, 270, 90);
            path.AddArc(x + s - r*2, y + s - r*2, r*2, r*2,   0, 90);
            path.AddArc(x,           y + s - r*2, r*2, r*2,  90, 90);
            path.CloseFigure();

            using var brBg = new LinearGradientBrush(
                new PointF(x, y), new PointF(x + s, y + s),
                Color.FromArgb(0, 55, 30), Color.FromArgb(0, 175, 85));
            g.FillPath(brBg, path);
            using var pBdr = new Pen(Color.FromArgb(60, 255, 255, 255), 1f);
            g.DrawPath(pBdr, path);

            float tw = s * 0.34f, th = s * 0.32f;
            float tx = x + (s - tw) / 2f - s * 0.02f;
            float ty = y + s * 0.13f + (s * 0.48f - th) / 2f;
            using var brW = new SolidBrush(Color.White);
            g.FillPolygon(brW, new PointF[] {
                new PointF(tx, ty), new PointF(tx, ty + th), new PointF(tx + tw, ty + th / 2f)
            });

            using var pLine = new Pen(Color.FromArgb(50, 255, 255, 255), s * 0.016f);
            g.DrawLine(pLine, x + s * 0.15f, y + s * 0.62f, x + s * 0.85f, y + s * 0.62f);

            using var fnt = new Font("Segoe UI", s * 0.17f, FontStyle.Bold);
            var szT = g.MeasureString("AMKVC", fnt);
            g.DrawString("AMKVC", fnt, brW,
                x + (s - Math.Min(szT.Width, s * 0.84f)) / 2f, y + s * 0.645f);
        }

        // ══════════════════════════════════════════════════════════════════
        //  DIBUJO: PANELES REDONDEADOS
        // ══════════════════════════════════════════════════════════════════
        private static void DibujarPanelRedondeado(Graphics g, Rectangle rc,
            Color fondo, Color borde, int radio)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var path = RectRedondeado(rc, radio);
            using var brFondo = new SolidBrush(fondo);
            g.FillPath(brFondo, path);
            using var pBorde = new Pen(borde, 1f);
            g.DrawPath(pBorde, path);
        }

        private static GraphicsPath RectRedondeado(Rectangle rc, int r)
        {
            var p = new GraphicsPath();
            p.AddArc(rc.X,                  rc.Y,                  r*2, r*2, 180, 90);
            p.AddArc(rc.Right - r*2,         rc.Y,                  r*2, r*2, 270, 90);
            p.AddArc(rc.Right - r*2,         rc.Bottom - r*2,       r*2, r*2,   0, 90);
            p.AddArc(rc.X,                  rc.Bottom - r*2,       r*2, r*2,  90, 90);
            p.CloseFigure();
            return p;
        }

        // ══════════════════════════════════════════════════════════════════
        //  DRAG & DROP
        // ══════════════════════════════════════════════════════════════════
        private void OnDragEnter(object sender, DragEventArgs e)
            => e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Copy : DragDropEffects.None;

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
            var lista = new List<string>();
            foreach (var p in paths) {
                if (File.Exists(p) && p.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase)) lista.Add(p);
                else if (Directory.Exists(p)) lista.AddRange(Directory.GetFiles(p, "*.mkv", SearchOption.AllDirectories));
            }
            if (lista.Count > 0) AgregarArchivos(lista.ToArray());
        }

        // ══════════════════════════════════════════════════════════════════
        //  BOTONES TOOLBAR
        // ══════════════════════════════════════════════════════════════════
        private void BtnAgregarFich_Click(object sender, EventArgs e)
        {
            using var dlg = new OpenFileDialog {
                Title = "Seleccionar archivos MKV",
                Filter = "Archivos MKV (*.mkv)|*.mkv|Todos|*.*", Multiselect = true
            };
            if (dlg.ShowDialog() == DialogResult.OK) AgregarArchivos(dlg.FileNames);
        }

        private void BtnAgregarCarp_Click(object sender, EventArgs e)
        {
            using var dlg = new FolderBrowserDialog { Description = "Carpeta con archivos MKV" };
            if (dlg.ShowDialog() == DialogResult.OK)
                AgregarArchivos(Directory.GetFiles(dlg.SelectedPath, "*.mkv", SearchOption.TopDirectoryOnly));
        }

        private void BtnEscanearCarp_Click(object sender, EventArgs e)
        {
            using var dlg = new FolderBrowserDialog {
                Description = "Selecciona carpeta raiz - se buscaran MKV en todas las subcarpetas"
            };
            if (dlg.ShowDialog() != DialogResult.OK) return;

            var encontrados = Directory.GetFiles(dlg.SelectedPath, "*.mkv", SearchOption.AllDirectories);
            int nuevos = encontrados.Count(p => !_archivos.Any(a => a.Ruta == p));

            if (encontrados.Length == 0) {
                MessageBox.Show(
                    "No se encontraron archivos .MKV en:\n" + dlg.SelectedPath + "\n\n(ni en sus subcarpetas)",
                    "Sin resultados", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string msg = nuevos == encontrados.Length
                ? "Se encontraron " + encontrados.Length + " archivo(s) MKV.\n\nAgregar todos a la lista?"
                : "Se encontraron " + encontrados.Length + " archivo(s) MKV.\n" +
                  (encontrados.Length - nuevos) + " ya estaban en la lista.\n\nAgregar los " + nuevos + " nuevo(s)?";

            if (MessageBox.Show(msg, "Escaneo completado",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                AgregarArchivos(encontrados);
        }

        private void BtnDestino_Click(object sender, EventArgs e)
        {
            using var dlg = new FolderBrowserDialog { Description = "Carpeta de destino" };
            if (dlg.ShowDialog() != DialogResult.OK) return;
            _carpetaDest = dlg.SelectedPath;
            SetStatus("Destino: " + _carpetaDest, false);
            btnDestino.Text = "Destino: " + Path.GetFileName(_carpetaDest) + "...";
        }

        private void BtnLimpiar_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("Limpiar la lista de archivos?", "Confirmar",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            _archivos.Clear();
            _actualizando = true;
            lvArchivos.Items.Clear();
            _actualizando = false;
            clbPistas.Items.Clear();
            pbGeneral.Value = 0;
            SetEstado("Listo para procesar.", "");
            ActualizarContadores();
        }

        private void BtnGuardarPerfil_Click(object sender, EventArgs e)
        {
            if (clbPistas.Items.Count == 0) {
                MessageBox.Show("No hay pistas cargadas.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using var dlg = new SaveFileDialog {
                Title = "Guardar perfil", Filter = "Perfil (*.mkvcl)|*.mkvcl",
                FileName = "mi_perfil", DefaultExt = "mkvcl"
            };
            if (dlg.ShowDialog() != DialogResult.OK) return;
            var perfil = new Dictionary<string, bool>();
            for (int i = 0; i < clbPistas.Items.Count; i++)
                perfil[clbPistas.Items[i].ToString()] = clbPistas.GetItemChecked(i);
            File.WriteAllText(dlg.FileName,
                JsonSerializer.Serialize(perfil, new JsonSerializerOptions { WriteIndented = true }));
            MessageBox.Show("Perfil guardado.", "OK", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void BtnCargarPerfil_Click(object sender, EventArgs e)
        {
            using var dlg = new OpenFileDialog {
                Title = "Cargar perfil", Filter = "Perfil (*.mkvcl)|*.mkvcl|JSON (*.json)|*.json"
            };
            if (dlg.ShowDialog() != DialogResult.OK) return;
            try {
                var perfil = JsonSerializer.Deserialize<Dictionary<string, bool>>(File.ReadAllText(dlg.FileName));
                for (int i = 0; i < clbPistas.Items.Count; i++) {
                    string key = clbPistas.Items[i].ToString();
                    if (perfil.TryGetValue(key, out bool chk)) clbPistas.SetItemChecked(i, chk);
                }
                MessageBox.Show("Perfil cargado.", "OK", MessageBoxButtons.OK, MessageBoxIcon.Information);
            } catch (Exception ex) {
                MessageBox.Show("Error:\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  ELIMINAR (Supr + menu contextual)
        // ══════════════════════════════════════════════════════════════════
        private void LvArchivos_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete) EliminarSeleccionados();
        }

        private void EliminarSeleccionados()
        {
            if (lvArchivos.SelectedItems.Count == 0) return;
            var aEliminar = lvArchivos.SelectedItems.Cast<ListViewItem>().ToList();
            foreach (var item in aEliminar) { var f = item.Tag as MkvFile; if (f != null) _archivos.Remove(f); }
            _actualizando = true;
            foreach (var item in aEliminar) lvArchivos.Items.Remove(item);
            _actualizando = false;
            RefrescarPistas();
            ActualizarContadores();
        }

        // ══════════════════════════════════════════════════════════════════
        //  CARGAR Y ESCANEAR
        // ══════════════════════════════════════════════════════════════════
        private async void AgregarArchivos(string[] paths)
        {
            btnAgregarFich.Enabled = btnAgregarCarp.Enabled = btnEscanearCarp.Enabled = false;
            SetEstado("Escaneando archivos...", "");
            var nuevos = paths.Where(p => !_archivos.Any(a => a.Ruta == p))
                .Select(p => new MkvFile(p)).ToList();
            if (nuevos.Count == 0) {
                btnAgregarFich.Enabled = btnAgregarCarp.Enabled = btnEscanearCarp.Enabled = true;
                SetEstado($"{_archivos.Count} archivo(s) cargado(s).", "");
                return;
            }
            foreach (var f in nuevos) _archivos.Add(f);
            RefrescarListaArchivos();

            // Escaneo paralelo (máx 4 simultáneos) — actualiza cada fila al terminar
            string mk = EncontrarMkvmerge();
            var sem = new SemaphoreSlim(4);
            var tareas = nuevos.Select(async f => {
                await sem.WaitAsync();
                try {
                    await Task.Run(() => EscanearPistas(f, mk));
                    Invoke((Action)(() => {
                        foreach (ListViewItem item in lvArchivos.Items) {
                            if (item.Tag == f) { item.SubItems[4].Text = f.Estado; break; }
                        }
                    }));
                } finally { sem.Release(); }
            });
            await Task.WhenAll(tareas);

            RefrescarPistas();
            ActualizarEstimaciones();
            ActualizarContadores();
            SetEstado($"{_archivos.Count} archivo(s) cargado(s).", "");
            btnAgregarFich.Enabled = btnAgregarCarp.Enabled = btnEscanearCarp.Enabled = true;
        }

        private void EscanearPistas(MkvFile f, string mk = null)
        {
            try {
                if (mk == null) mk = EncontrarMkvmerge();
                if (mk == null) { f.Estado = "mkvmerge no encontrado"; return; }
                var psi = new ProcessStartInfo(mk, $"-J \"{f.Ruta}\"") {
                    RedirectStandardOutput = true, UseShellExecute = false,
                    CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8
                };
                using var proc = Process.Start(psi);
                string json = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();
                f.Pistas = ParsearPistas(json);
                f.Estado = $"{f.Pistas.Count} pista(s)";
            } catch (Exception ex) { f.Estado = "Error: " + ex.Message; }
        }

        private List<PistaInfo> ParsearPistas(string json)
        {
            var pistas = new List<PistaInfo>();
            int idx = json.IndexOf("\"tracks\"");
            if (idx < 0) return pistas;
            int depth = 0, start = -1;
            for (int i = idx; i < json.Length; i++) {
                if (json[i] == '{') { if (depth++ == 0) start = i; }
                else if (json[i] == '}' && --depth == 0 && start >= 0) {
                    string blk = json.Substring(start, i - start + 1);
                    var mTipo  = Regex.Match(blk, "\"type\"\\s*:\\s*\"(?<t>[^\"]+)\"");
                    var mId    = Regex.Match(blk, "\"id\"\\s*:\\s*(?<id>\\d+)");
                    var mCodec = Regex.Match(blk, "\"codec\"\\s*:\\s*\"(?<c>[^\"]+)\"");
                    var mLang  = Regex.Match(blk, "\"language\"\\s*:\\s*\"(?<l>[^\"]+)\"");
                    var mName  = Regex.Match(blk, "\"track_name\"\\s*:\\s*\"(?<n>[^\"]+)\"");
                    if (mTipo.Success && mId.Success)
                        pistas.Add(new PistaInfo {
                            Id = int.Parse(mId.Groups["id"].Value),
                            Tipo   = mTipo.Groups["t"].Value,
                            Codec  = mCodec.Success ? mCodec.Groups["c"].Value : "?",
                            Idioma = mLang.Success  ? mLang.Groups["l"].Value  : "und",
                            Nombre = mName.Success  ? mName.Groups["n"].Value  : ""
                        });
                    start = -1;
                }
            }
            return pistas;
        }

        // ══════════════════════════════════════════════════════════════════
        //  REFRESCO
        // ══════════════════════════════════════════════════════════════════
        private void RefrescarListaArchivos()
        {
            if (InvokeRequired) { Invoke((Action)RefrescarListaArchivos); return; }
            _actualizando = true;
            lvArchivos.BeginUpdate();
            lvArchivos.Items.Clear();
            foreach (var f in _archivos) {
                long tam = f.TamanoReal > 0 ? f.TamanoReal : (f.TamanoReal = new FileInfo(f.Ruta).Length);
                var item = new ListViewItem(Path.GetFileName(f.Ruta)) { Tag = f, Checked = true };
                item.SubItems.Add(FormatSize(tam));
                item.SubItems.Add(f.TamanoEstimado > 0 ? FormatSize(f.TamanoEstimado) : "—");
                item.SubItems.Add(f.Ahorro > 0 ? $"-{f.Ahorro:F0}%" : "—");
                item.SubItems.Add(f.Estado);
                lvArchivos.Items.Add(item);
            }
            lvArchivos.EndUpdate();
            _actualizando = false;
        }

        private void RefrescarPistas()
        {
            if (InvokeRequired) { Invoke((Action)RefrescarPistas); return; }
            var activos = lvArchivos.Items.Cast<ListViewItem>()
                .Where(i => i.Checked).Select(i => i.Tag as MkvFile)
                .Where(f => f != null).ToList();

            // Sin archivos marcados → panel vacío
            if (activos.Count == 0) {
                clbPistas.Items.Clear();
                return;
            }

            var claves = new HashSet<string>();
            foreach (var f in activos) foreach (var p in f.Pistas) claves.Add(p.ClaveUnica);

            var estado = new Dictionary<string, bool>();
            for (int i = 0; i < clbPistas.Items.Count; i++)
                estado[clbPistas.Items[i].ToString()] = clbPistas.GetItemChecked(i);

            int OrdenTipo(string c) {
                if (c.StartsWith("[Video]"))      return 0;
                if (c.StartsWith("[Audio]"))      return 1;
                if (c.StartsWith("[Subtitulos]")) return 2;
                return 3;
            }
            clbPistas.BeginUpdate();
            clbPistas.Items.Clear();
            foreach (var c in claves.OrderBy(OrdenTipo).ThenBy(x => x))
                clbPistas.Items.Add(c, estado.TryGetValue(c, out bool prev) ? prev : true);
            clbPistas.EndUpdate();
        }

        private void LvArchivos_ItemChecked(object sender, ItemCheckedEventArgs e)
        {
            if (_actualizando) return;
            this.BeginInvoke((Action)(() => {
                RefrescarPistas();
                ActualizarEstimaciones();
            }));
        }

        private void ClbPistas_ItemCheck(object sender, ItemCheckEventArgs e)
            => this.BeginInvoke((Action)(() => ActualizarEstimaciones()));

        private void ActualizarEstimaciones()
        {
            var conservar = PistasSeleccionadas();
            _actualizando = true;
            lvArchivos.BeginUpdate();
            foreach (ListViewItem item in lvArchivos.Items) {
                var f = item.Tag as MkvFile;
                if (f == null) continue;
                if (!item.Checked) {
                    // Archivo desmarcado: muestra tamaño original y sin ahorro
                    long tam = f.TamanoReal > 0 ? f.TamanoReal : (f.TamanoReal = new FileInfo(f.Ruta).Length);
                    item.SubItems[2].Text = FormatSize(tam);
                    item.SubItems[3].Text = "—";
                } else {
                    CalcularAhorro(f, conservar);
                    item.SubItems[2].Text = f.TamanoEstimado > 0 ? FormatSize(f.TamanoEstimado) : "—";
                    item.SubItems[3].Text = f.Ahorro > 0 ? $"-{f.Ahorro:F0}%" : "—";
                }
            }
            lvArchivos.EndUpdate();
            _actualizando = false;
        }

        private HashSet<string> PistasSeleccionadas()
        {
            var set = new HashSet<string>();
            for (int i = 0; i < clbPistas.Items.Count; i++)
                if (clbPistas.GetItemChecked(i)) set.Add(clbPistas.Items[i].ToString());
            return set;
        }

        private void CalcularAhorro(MkvFile f, HashSet<string> conservar)
        {
            if (f.Pistas.Count == 0) return;
            long tamTotal = f.TamanoReal > 0 ? f.TamanoReal : (f.TamanoReal = new FileInfo(f.Ruta).Length);

            var videos = f.Pistas.Where(p => p.Tipo == "video").ToList();
            var audios = f.Pistas.Where(p => p.Tipo == "audio").ToList();
            var subs   = f.Pistas.Where(p => p.Tipo == "subtitles").ToList();

            // Pesos típicos en un MKV con video
            double pesoVideo = 0.8217, pesoAudio = 0.1683, pesoSubs = 0.00525;
            double overhead  = 1.0 - pesoVideo - pesoAudio - pesoSubs; // ~0.5% metadatos inamovibles

            double fracConservar = overhead;

            // Video: reparto uniforme entre pistas de video
            if (videos.Count > 0) {
                int ok = videos.Count(p => conservar.Contains(p.ClaveUnica));
                fracConservar += pesoVideo * ((double)ok / videos.Count);
            }

            // Audio: pesos por codec (lossless pesa mucho más que lossy)
            if (audios.Count > 0) {
                double[] puntos = audios.Select((a, i) => {
                    string c = (a.Codec ?? "").ToUpperInvariant();
                    double p = c.Contains("TRUEHD") || c.Contains("MLP")        ? 5.0
                             : c.Contains("DTS-MA") || c.Contains("DTS:X")      ? 4.5
                             : c.Contains("PCM")    || c.Contains("FLAC")       ? 4.0
                             : c.Contains("DTS")                                 ? 3.0
                             : c.Contains("EAC3")   || c.Contains("AC-3") || c.Contains("AC3") ? 2.0
                             : c.Contains("AAC")                                 ? 1.2
                             : c.Contains("MP3")    || c.Contains("VORBIS")     ? 0.8
                             : 1.0;
                    return p * (i == 0 ? 1.3 : 1.0); // primera pista suele ser la principal
                }).ToArray();
                double total = puntos.Sum();
                for (int i = 0; i < audios.Count; i++)
                    if (conservar.Contains(audios[i].ClaveUnica))
                        fracConservar += pesoAudio * (puntos[i] / total);
            }

            // Subtítulos: reparto uniforme (pesan muy poco)
            if (subs.Count > 0) {
                int ok = subs.Count(p => conservar.Contains(p.ClaveUnica));
                fracConservar += pesoSubs * ((double)ok / subs.Count);
            }

            fracConservar = Math.Max(0.01, Math.Min(1.0, fracConservar));
            f.TamanoEstimado = (long)(tamTotal * fracConservar);
            f.Ahorro = (1.0 - fracConservar) * 100.0;
        }

        // ══════════════════════════════════════════════════════════════════
        //  MULTIPLEXADO
        // ══════════════════════════════════════════════════════════════════
        private async void BtnMultiplexar_Click(object sender, EventArgs e)
        {
            if (_archivos.Count == 0) {
                MessageBox.Show("No hay archivos cargados.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var conservar = PistasSeleccionadas();
            if (conservar.Count == 0) {
                MessageBox.Show("Debes conservar al menos una pista.", "Aviso",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning); return;
            }
            string mk = EncontrarMkvmerge();
            if (mk == null) {
                MessageBox.Show("No se encontro mkvmerge.exe.", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error); return;
            }
            var archivos = lvArchivos.Items.Cast<ListViewItem>()
                .Where(i => i.Checked).Select(i => i.Tag as MkvFile)
                .Where(f => f != null).ToList();
            if (archivos.Count == 0) {
                MessageBox.Show("Marca al menos un archivo.", "Aviso",
                    MessageBoxButtons.OK, MessageBoxIcon.Information); return;
            }

            // Comprobar si ya existen ficheros _clean.mkv
            string destDirCheck = string.IsNullOrEmpty(_carpetaDest)
                ? null : _carpetaDest;
            var yaExisten = archivos.Where(f => {
                string dir = string.IsNullOrEmpty(_carpetaDest)
                    ? Path.GetDirectoryName(f.Ruta) : _carpetaDest;
                string salida = Path.Combine(dir, Path.GetFileNameWithoutExtension(f.Ruta) + "_clean.mkv");
                return File.Exists(salida);
            }).ToList();
            if (yaExisten.Count > 0) {
                string lista = string.Join("\n", yaExisten.Select(f =>
                    Path.GetFileNameWithoutExtension(f.Ruta) + "_clean.mkv"));
                var resp = MessageBox.Show(
                    $"Los siguientes ficheros ya existen y serán sobreescritos:\n\n{lista}\n\n¿Continuar?",
                    "Ficheros existentes", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (resp == DialogResult.No) return;
            }

            _cts = new CancellationTokenSource();
            btnMultiplexar.Enabled = btnAgregarFich.Enabled = btnAgregarCarp.Enabled = btnEscanearCarp.Enabled = false;
            btnDetener.Enabled = true;
            pbGeneral.Minimum  = 0;
            pbGeneral.Maximum  = archivos.Count * 100;
            pbGeneral.Value    = 0;

            int ok = 0, errores = 0;
            await Task.Run(async () => {
                for (int idx = 0; idx < archivos.Count; idx++) {
                    if (_cts.Token.IsCancellationRequested) break;
                    var f = archivos[idx];
                    Invoke((Action)(() => {
                        SetEstado($"[{idx + 1}/{archivos.Count}]  {Path.GetFileName(f.Ruta)}", "");
                        ActualizarItemEstado(f, "Procesando... 0%", Color.FromArgb(220, 100, 0));
                        pbGeneral.Value = idx * 100;
                    }));
                    int baseVal = idx * 100;
                    string err = await ProcesarArchivoAsync(mk, f, conservar, _cts.Token,
                        pct => Invoke((Action)(() => {
                            int v = baseVal + pct;
                            pbGeneral.Value = Math.Min(v, pbGeneral.Maximum);
                            ActualizarItemEstado(f, $"Procesando... {pct}%", Color.FromArgb(220, 100, 0));
                        })));
                    bool bien = err == null;
                    if (bien) ok++; else errores++;
                    Invoke((Action)(() => {
                        ActualizarItemEstado(f, bien ? "OK" : "Error: " + err,
                            bien ? CVerde : Color.FromArgb(190, 50, 50));
                        pbGeneral.Value = (idx + 1) * 100;
                    }));
                }
            });

            btnMultiplexar.Enabled = btnAgregarFich.Enabled = btnAgregarCarp.Enabled = btnEscanearCarp.Enabled = true;
            btnDetener.Enabled = false; _cts = null;
            string resumen = errores == 0
                ? $"Completado: {ok} archivo(s) procesado(s)."
                : $"Finalizado: {ok} correctos, {errores} con error(es).";
            SetEstado(resumen, "");
            MessageBox.Show(resumen, "Resultado", MessageBoxButtons.OK,
                errores == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        private async Task<string> ProcesarArchivoAsync(
            string mk, MkvFile f, HashSet<string> conservar,
            CancellationToken ct, Action<int> progreso)
        {
            try {
                var video = f.Pistas.Where(p => p.Tipo == "video"     && conservar.Contains(p.ClaveUnica)).Select(p => p.Id.ToString()).ToList();
                var audio = f.Pistas.Where(p => p.Tipo == "audio"     && conservar.Contains(p.ClaveUnica)).Select(p => p.Id.ToString()).ToList();
                var subs  = f.Pistas.Where(p => p.Tipo == "subtitles" && conservar.Contains(p.ClaveUnica)).Select(p => p.Id.ToString()).ToList();
                string destDir = string.IsNullOrEmpty(_carpetaDest)
                    ? Path.GetDirectoryName(f.Ruta) : _carpetaDest;
                string salida = Path.Combine(destDir,
                    Path.GetFileNameWithoutExtension(f.Ruta) + "_clean.mkv");
                var args = new StringBuilder($"-o \"{salida}\"");
                args.Append(video.Count > 0 ? $" --video-tracks {string.Join(",", video)}"    : " --no-video");
                args.Append(audio.Count > 0 ? $" --audio-tracks {string.Join(",", audio)}"    : " --no-audio");
                args.Append(subs.Count  > 0 ? $" --subtitle-tracks {string.Join(",", subs)}"  : " --no-subtitles");
                args.Append($" \"{f.Ruta}\"");
                var psi = new ProcessStartInfo(mk, args.ToString()) {
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    UseShellExecute = false, CreateNoWindow = true
                };
                var proc = Process.Start(psi);
                _ = Task.Run(() => {
                    var sb = new StringBuilder();
                    int ch;
                    while ((ch = proc.StandardOutput.Read()) != -1) {
                        if (ch == '\r' || ch == '\n') {
                            string line = sb.ToString();
                            sb.Clear();
                            var m = Regex.Match(line, @"Progres[os]e?:\s*(\d+)%");
                            if (m.Success && int.TryParse(m.Groups[1].Value, out int pct))
                                progreso(pct);
                        } else {
                            sb.Append((char)ch);
                        }
                    }
                });
                await Task.Run(() => proc.WaitForExit(), ct);
                if (ct.IsCancellationRequested) { try { proc.Kill(); } catch { } return "Cancelado"; }
                return proc.ExitCode == 0 ? null : $"Codigo {proc.ExitCode}";
            } catch (Exception ex) { return ex.Message; }
        }

        private void BtnDetener_Click(object sender, EventArgs e)
        {
            _cts?.Cancel();
            SetEstado("Cancelando...", "");
        }

        // ══════════════════════════════════════════════════════════════════
        //  UTILIDADES
        // ══════════════════════════════════════════════════════════════════
        private string EncontrarMkvmerge()
        {
            if (!string.IsNullOrEmpty(_mkvmergePath) && File.Exists(_mkvmergePath)) return _mkvmergePath;
            string local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mkvmerge.exe");
            if (File.Exists(local)) return local;
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';')) {
                string full = Path.Combine(dir.Trim(), "mkvmerge.exe");
                if (File.Exists(full)) return full;
            }
            foreach (var c in new[] {
                @"C:\Program Files\MKVToolNix\mkvmerge.exe",
                @"C:\Program Files (x86)\MKVToolNix\mkvmerge.exe" })
                if (File.Exists(c)) return c;
            return null;
        }

        private void SetStatus(string texto, bool alerta)
        {
            if (InvokeRequired) { Invoke((Action<string, bool>)SetStatus, texto, alerta); return; }
            lblStatus.Text      = texto;
            lblStatus.ForeColor = alerta ? Color.FromArgb(220, 160, 60) : Color.FromArgb(185, 185, 185);
        }

        private void ActualizarContadores()
        {
            if (InvokeRequired) { Invoke((Action)ActualizarContadores); return; }
            int n = _archivos.Count;
            lblContadores.Text = n == 0 ? "0 archivos" : $"{n} archivo(s)";
            lblStatus.Text = n == 0
                ? "Andalusian MKV Cleaner  |  Carga archivos para comenzar"
                : $"Andalusian MKV Cleaner  |  {n} archivo(s) en lista";
        }

        private void SetEstado(string l1, string l2)
        {
            if (InvokeRequired) { Invoke((Action<string, string>)SetEstado, l1, l2); return; }
            lblArchivoActual.Text = l1;
            lblProgreso.Text      = l2;
        }

        private void ActualizarItemEstado(MkvFile f, string estado, Color color)
        {
            if (InvokeRequired) { Invoke((Action<MkvFile, string, Color>)ActualizarItemEstado, f, estado, color); return; }
            foreach (ListViewItem item in lvArchivos.Items)
                if (item.Tag == f) { item.SubItems[4].Text = estado; item.ForeColor = color; break; }
        }

        private static string FormatSize(long bytes)
        {
            if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824.0:F2} GB";
            if (bytes >= 1_048_576)     return $"{bytes / 1_048_576.0:F0} MB";
            return $"{bytes / 1024.0:F0} KB";
        }

        // Boton verde principal
        private Button CrearBotonVerde(string texto, int ancho)
        {
            var b = new Button {
                Text = texto, BackColor = CVerde, ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat, Size = new Size(ancho, 30),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Cursor = Cursors.Hand, UseVisualStyleBackColor = false
            };
            b.FlatAppearance.BorderColor = CVerdeOsc;
            b.FlatAppearance.BorderSize  = 1;
            return b;
        }

        // Boton secundario (gris claro)
        private Button CrearBotonSecundario(string texto, int ancho)
        {
            var b = new Button {
                Text = texto, BackColor = CBtnSecBg, ForeColor = CBtnSecText,
                FlatStyle = FlatStyle.Flat, Size = new Size(ancho, 30),
                Font = new Font("Segoe UI", 8.5f),
                Cursor = Cursors.Hand, UseVisualStyleBackColor = false
            };
            b.FlatAppearance.BorderColor = CBtnSecBorde;
            b.FlatAppearance.BorderSize  = 1;
            return b;
        }

        // Boton peligro (rojo suave)
        private Button CrearBotonPeligro(string texto, int ancho)
        {
            var b = new Button {
                Text = texto, BackColor = Color.FromArgb(255, 245, 245),
                ForeColor = Color.FromArgb(160, 40, 40),
                FlatStyle = FlatStyle.Flat, Size = new Size(ancho, 30),
                Font = new Font("Segoe UI", 8.5f),
                Cursor = Cursors.Hand, UseVisualStyleBackColor = false
            };
            b.FlatAppearance.BorderColor = Color.FromArgb(220, 190, 190);
            b.FlatAppearance.BorderSize  = 1;
            return b;
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  MODELOS
    // ════════════════════════════════════════════════════════════════════════
    public class MkvFile
    {
        public string          Ruta           { get; }
        public List<PistaInfo> Pistas         { get; set; } = new List<PistaInfo>();
        public string          Estado         { get; set; } = "Escaneando...";
        public long            TamanoEstimado { get; set; } = 0;
        public long            TamanoReal     { get; set; } = 0;
        public double          Ahorro         { get; set; } = 0;
        public MkvFile(string ruta) => Ruta = ruta;
    }

    public class PistaInfo
    {
        public int    Id     { get; set; }
        public string Tipo   { get; set; }
        public string Codec  { get; set; }
        public string Idioma { get; set; }
        public string Nombre { get; set; }

        public string ClaveUnica
        {
            get {
                string cat = Tipo switch {
                    "video"     => "[Video]",
                    "audio"     => "[Audio]",
                    "subtitles" => "[Subtitulos]",
                    _           => "[Otro]"
                };
                string extra = string.IsNullOrEmpty(Nombre) ? "" : $" ({Nombre})";
                return $"{cat} {Codec.ToUpperInvariant()} - {Idioma}{extra}";
            }
        }
    }
}
