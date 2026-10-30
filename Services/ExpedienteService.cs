using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using CajaAhorro.Models;

namespace CajaAhorro.Services;

public class ExpedienteService
{
    // ── Datos institucionales (nombre CORRECTO: SNTSA) ───────────────────────
    private const string SINDICATO = "SINDICATO NACIONAL DE TRABAJADORES DE LA SECRETARÍA DE SALUD";
    private const string SECCION   = "SECCIÓN 39";
    private const string CAJA      = "CAJA DE AHORRO";
    private const string CIUDAD    = "GUADALUPE, ZAC.";
    private const string BANCO     = "HSBC";
    private const string CUENTA    = "04054697156";

    // Paleta
    private const string AZUL    = "#1F3864";
    private const string AZULTEN = "#EAF0F8";
    private const string GRIS    = "#9CA3AF";
    private const string GRISCL  = "#F3F4F6";
    private const string ROJO    = "#9E1B1B";

    private readonly byte[]? _logo;

    public ExpedienteService(IWebHostEnvironment env)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        // Cargar el logo del sindicato una sola vez (si existe en wwwroot/img).
        try
        {
            var ruta = Path.Combine(env.WebRootPath ?? "wwwroot", "img", "logo-sntsa.png");
            if (File.Exists(ruta))
                _logo = File.ReadAllBytes(ruta);
        }
        catch { _logo = null; }
    }

    public byte[] GenerarExpedienteCompleto(Prestamo p, List<AmortizacionDetalle> amortizacion)
    {
        return Document.Create(container =>
        {
            container.Page(page => PaginaDescuento(page, p));
            container.Page(page => PaginaPagare(page, p));
            container.Page(page => PaginaPoliza(page, p));
            container.Page(page => PaginaAmortizacion(page, p, amortizacion));
        }).GeneratePdf();
    }

    // ── ENCABEZADO OFICIAL REUTILIZABLE (logo + 3 líneas) ────────────────────
    private void EncabezadoOficial(ColumnDescriptor col, string subtitulo)
    {
        col.Item().Row(row =>
        {
            // Logo a la izquierda
            row.ConstantItem(58).AlignMiddle().Element(c =>
            {
                if (_logo is not null)
                    c.Height(54).Image(_logo).FitHeight();
                else
                    c.Height(54);
            });

            // Títulos centrados
            row.RelativeItem().Column(t =>
            {
                t.Item().AlignCenter().Text(SINDICATO).Bold().FontSize(11).FontColor(AZUL);
                t.Item().AlignCenter().Text($"{SECCION} · {CAJA}").Bold().FontSize(10).FontColor(AZUL);
            });

            // Espaciador simétrico al logo
            row.ConstantItem(58);
        });

        col.Item().PaddingTop(4).LineHorizontal(1.5f).LineColor(AZUL);

        col.Item().PaddingTop(8).AlignCenter()
            .Text(subtitulo).Bold().FontSize(13).FontColor(ROJO).LetterSpacing(0.03f);
    }

    // Celda etiqueta/valor con bordes (estilo formato oficial)
    private static void FilaDato(TableDescriptor t, string etiqueta, string valor)
    {
        t.Cell().Border(0.5f).BorderColor(GRIS).Background(GRISCL)
            .PaddingVertical(4).PaddingHorizontal(8)
            .Text(etiqueta).Bold().FontSize(9);
        t.Cell().Border(0.5f).BorderColor(GRIS)
            .PaddingVertical(4).PaddingHorizontal(8)
            .Text(valor ?? "").FontSize(9);
    }

    // ── 1. FORMATO DE DESCUENTO ──────────────────────────────────────────────
    private void PaginaDescuento(PageDescriptor page, Prestamo p)
    {
        page.Size(PageSizes.Letter);
        page.Margin(2, Unit.Centimetre);
        page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Arial").FontColor("#1A1A2E"));

        page.Content().Column(col =>
        {
            EncabezadoOficial(col, "FORMATO DE DESCUENTO");

            col.Item().PaddingTop(14).Background(AZULTEN).Border(0.5f).BorderColor(GRIS)
                .Padding(10).Text(txt =>
                {
                    txt.Span("A TRAVÉS DE ESTE MEDIO ").FontSize(9);
                    txt.Span("ACEPTO Y AUTORIZO").Bold().FontSize(9);
                    txt.Span(" para que las amortizaciones de mi crédito contraído con la Caja de Ahorro " +
                             "se me descuenten de mi percepción ordinaria, vía nómina y de manera quincenal.").FontSize(9);
                });

            // Montos
            col.Item().PaddingTop(14).Table(t =>
            {
                t.ColumnsDefinition(c => { c.RelativeColumn(3); c.RelativeColumn(2); });
                FilaDato(t, "MONTO DEL DESCUENTO QUINCENAL", $"$ {p.PagoQuincenal:N2}");
                FilaDato(t, "TOTAL DE QUINCENAS A DESCONTAR", p.NumeroDescuentos.ToString());
            });

            var fechaIni = FechaPrimerDiaQuincena(p.QuincenaInicial, p.AnioInicial);
            var fechaFin = FechaUltimoDiaQuincena(p.QuincenaFinal, p.AnioFinal);

            col.Item().PaddingTop(12).Table(t =>
            {
                t.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(120); c.RelativeColumn(); c.RelativeColumn();
                    c.RelativeColumn(); c.RelativeColumn(); c.RelativeColumn();
                });

                void Th(string s) => t.Cell().Border(0.5f).BorderColor(GRIS).Background(AZUL)
                    .PaddingVertical(3).AlignCenter().Text(s).Bold().FontSize(8).FontColor("#FFFFFF");
                void Td(string s) => t.Cell().Border(0.5f).BorderColor(GRIS)
                    .PaddingVertical(3).AlignCenter().Text(s).FontSize(9);

                Th(""); Th("QUINCENA"); Th("AÑO"); Th("DÍA"); Th("MES"); Th("AÑO");

                t.Cell().Border(0.5f).BorderColor(GRIS).Background(GRISCL)
                    .PaddingVertical(3).PaddingHorizontal(6).Text("QUINCENA INICIAL").Bold().FontSize(9);
                Td(p.QuincenaInicial.ToString("D2")); Td(p.AnioInicial.ToString());
                Td(fechaIni.Day.ToString()); Td(NombreMes(fechaIni.Month)); Td(p.AnioInicial.ToString());

                t.Cell().Border(0.5f).BorderColor(GRIS).Background(GRISCL)
                    .PaddingVertical(3).PaddingHorizontal(6).Text("QUINCENA FINAL").Bold().FontSize(9);
                Td(p.QuincenaFinal.ToString("D2")); Td(p.AnioFinal.ToString());
                Td(fechaFin.Day.ToString()); Td(NombreMes(fechaFin.Month)); Td(p.AnioFinal.ToString());
            });

            col.Item().PaddingTop(16).Text("PARA LO CUAL ANEXO MIS DATOS GENERALES:").Bold().FontSize(10);

            col.Item().PaddingTop(8).Table(t =>
            {
                t.ColumnsDefinition(c => { c.ConstantColumn(170); c.RelativeColumn(); });
                FilaDato(t, "NOMBRE", p.Socio.Nombre);
                FilaDato(t, "R.F.C.", p.Socio.RFC);
                FilaDato(t, "ADSCRIPCIÓN", p.Socio.Adscripcion);
                FilaDato(t, "TIPO DE NOMBRAMIENTO", p.Socio.TipoNombramiento);
                FilaDato(t, "JORNADA LABORAL", p.Socio.JornadaLaboral);
            });

            col.Item().PaddingTop(18).Text(
                $"De conformidad suscribo el presente documento en {CIUDAD} a los " +
                $"{p.FechaSolicitud.Day} días del mes de {NombreMes(p.FechaSolicitud.Month).ToUpper()} del {p.FechaSolicitud.Year}.")
                .FontSize(10);

            col.Item().PaddingTop(45).Table(t =>
            {
                t.ColumnsDefinition(c => { c.RelativeColumn(); c.ConstantColumn(40); c.RelativeColumn(); });

                t.Cell().AlignCenter().Column(f =>
                {
                    f.Item().LineHorizontal(0.8f);
                    f.Item().PaddingTop(3).AlignCenter().Text(p.Socio.Nombre).Bold().FontSize(9);
                    f.Item().AlignCenter().Text("FIRMA DEL TRABAJADOR").FontSize(8).FontColor(GRIS);
                });
                t.Cell();
                t.Cell().AlignCenter().Column(f =>
                {
                    f.Item().AlignCenter().Text(p.NumeroChequeFolio ?? "").Bold().FontSize(11);
                    f.Item().LineHorizontal(0.8f);
                    f.Item().AlignCenter().Text("NO. DE CHEQUE / REFERENCIA").FontSize(8).FontColor(GRIS);
                });
            });
        });
    }

    // ── 2. PAGARÉ ────────────────────────────────────────────────────────────
    private void PaginaPagare(PageDescriptor page, Prestamo p)
    {
        page.Size(PageSizes.Letter);
        page.Margin(2, Unit.Centimetre);
        page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Arial").FontColor("#1A1A2E"));

        var fechaVenc      = FechaUltimoDiaQuincena(p.QuincenaFinal, p.AnioFinal);
        var totalIntereses = p.TotalAPagar - p.ImporteOtorgado;
        var tasaTotalPct   = p.TasaInteres * (p.NumeroDescuentos + 2) * 100m;  // ej. 78%
        var tasaMensualPct = p.TasaInteres * 2 * 100m;                         // ej. 6%

        page.Content().Column(col =>
        {
            // Banda superior con logo y título
            col.Item().Row(row =>
            {
                row.ConstantItem(58).AlignMiddle().Element(c =>
                { if (_logo is not null) c.Height(50).Image(_logo).FitHeight(); else c.Height(50); });
                row.RelativeItem().AlignMiddle().AlignCenter()
                    .Text("P A G A R É").Bold().FontSize(20).FontColor(AZUL).LetterSpacing(0.1f);
                row.ConstantItem(58);
            });

            col.Item().PaddingTop(6).LineHorizontal(1.5f).LineColor(AZUL);

            // Cuadro de importes
            col.Item().PaddingTop(12).Table(t =>
            {
                t.ColumnsDefinition(c => { c.ConstantColumn(110); c.RelativeColumn(); c.ConstantColumn(120); });

                t.Cell().Border(0.5f).BorderColor(GRIS).Background(GRISCL)
                    .Padding(6).Text("PAGARÉ No.").Bold().FontSize(9);
                t.Cell().Border(0.5f).BorderColor(GRIS).Padding(6).Text("1 / 1").FontSize(9);
                t.Cell().RowSpan(3).Border(0.5f).BorderColor(GRIS).Padding(8).Column(f =>
                {
                    f.Item().Text("CAPITAL").FontSize(8).FontColor(GRIS);
                    f.Item().Text($"$ {p.ImporteOtorgado:N2}").Bold().FontSize(11);
                    f.Item().PaddingTop(4).Text($"INTERÉS TOTAL ({tasaTotalPct:N1}%)").FontSize(8).FontColor(GRIS);
                    f.Item().Text($"$ {totalIntereses:N2}").Bold().FontSize(11);
                    f.Item().PaddingTop(4).Text("TOTAL").FontSize(8).FontColor(GRIS);
                    f.Item().Text($"$ {p.TotalAPagar:N2}").Bold().FontSize(13).FontColor(ROJO);
                });

                t.Cell().Border(0.5f).BorderColor(GRIS).Background(GRISCL)
                    .Padding(6).Text("LUGAR Y FECHA").Bold().FontSize(9);
                t.Cell().Border(0.5f).BorderColor(GRIS).Padding(6)
                    .Text($"{CIUDAD}  a  {p.FechaSolicitud.Day} de {NombreMes(p.FechaSolicitud.Month)} del {p.FechaSolicitud.Year}").FontSize(9);

                t.Cell().Border(0.5f).BorderColor(GRIS).Background(GRISCL)
                    .Padding(6).Text("VENCIMIENTO").Bold().FontSize(9);
                t.Cell().Border(0.5f).BorderColor(GRIS).Padding(6)
                    .Text($"{fechaVenc.Day} de {NombreMes(fechaVenc.Month)} del {fechaVenc.Year}").FontSize(9);
            });

            col.Item().PaddingTop(14).AlignCenter()
                .Text("DEBO Y PAGARÉ INCONDICIONALMENTE POR ESTE PAGARÉ A LA ORDEN DE:").Bold().FontSize(10);
            col.Item().PaddingTop(3).AlignCenter()
                .Text($"{SINDICATO} {SECCION}").Bold().FontSize(10).FontColor(AZUL);

            col.Item().PaddingTop(12).Background(AZULTEN).Border(0.5f).BorderColor(GRIS).Padding(8)
                .Text(txt =>
                {
                    txt.Span("La cantidad de ").FontSize(10);
                    txt.Span($"$ {p.ImporteOtorgado:N2}").Bold().FontSize(10);
                    txt.Span($" a una tasa de {tasaMensualPct:N0}% de interés mensual.").FontSize(10);
                });

            col.Item().PaddingTop(8).AlignCenter()
                .Text(NumeroALetras(p.TotalAPagar).ToUpper()).Bold().FontSize(11).FontColor(AZUL);

            col.Item().PaddingTop(12).Text(
                "Valor recibido a mi entera satisfacción. Este pagaré forma parte de una serie numerada del 1 al 1 " +
                "y todos están sujetos a la condición de que, al no pagarse cualquiera de ellos a su vencimiento, " +
                "serán exigibles todos los que le sigan en número, además de los ya vencidos. Desde la fecha de " +
                "vencimiento de este documento hasta el día de su liquidación, causará intereses moratorios al tipo " +
                "del 4% mensual, pagadero en esta ciudad juntamente con el principal.").FontSize(8.5f);

            col.Item().PaddingTop(20).Table(t =>
            {
                t.ColumnsDefinition(c => { c.RelativeColumn(); c.RelativeColumn(); });

                t.Cell().Border(0.5f).BorderColor(GRIS).Padding(10).Column(f =>
                {
                    f.Item().Text("NOMBRE Y DATOS DEL DEUDOR").Bold().FontSize(9).FontColor(AZUL);
                    f.Item().PaddingTop(6).Text($"Nombre:  {p.Socio.Nombre}").FontSize(9);
                    f.Item().PaddingTop(3).Text($"Domicilio:  {($"{p.Socio.Calle} {p.Socio.Numero}").Trim()}").FontSize(9);
                    f.Item().PaddingTop(3).Text($"Adscripción:  {p.Socio.Adscripcion}").FontSize(9);
                    f.Item().PaddingTop(3).Text($"Teléfono:  {p.Socio.TelefonoCelular}").FontSize(9);
                    f.Item().PaddingTop(3).Text($"R.F.C.:  {p.Socio.RFC}").FontSize(9);
                });

                t.Cell().Border(0.5f).BorderColor(GRIS).Padding(10).AlignCenter().AlignMiddle().Column(f =>
                {
                    f.Item().PaddingBottom(35).AlignCenter().Text("ACEPTO").Bold().FontSize(9).FontColor(AZUL);
                    f.Item().LineHorizontal(0.8f);
                    f.Item().PaddingTop(3).AlignCenter().Text("FIRMA DEL DEUDOR").FontSize(8).FontColor(GRIS);
                });
            });
        });
    }

    // ── 3. PÓLIZA DE CHEQUE ──────────────────────────────────────────────────
    private void PaginaPoliza(PageDescriptor page, Prestamo p)
    {
        page.Size(PageSizes.Letter);
        page.Margin(2, Unit.Centimetre);
        page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Arial").FontColor("#1A1A2E"));

        page.Content().Column(col =>
        {
            EncabezadoOficial(col, "PÓLIZA DE CHEQUE");

            // Datos del cheque
            col.Item().PaddingTop(14).Table(t =>
            {
                t.ColumnsDefinition(c => { c.RelativeColumn(); c.RelativeColumn(); c.ConstantColumn(150); });

                FilaDato(t, "BANCO", BANCO);
                t.Cell().RowSpan(2).Border(0.5f).BorderColor(GRIS).Background(AZULTEN)
                    .Padding(8).AlignMiddle().Column(f =>
                {
                    f.Item().AlignCenter().Text("BUENO POR").FontSize(9).FontColor(GRIS);
                    f.Item().AlignCenter().Text($"$ {p.ImporteOtorgado:N2}").Bold().FontSize(15).FontColor(ROJO);
                });
                FilaDato(t, "CUENTA No.", CUENTA);
            });

            col.Item().PaddingTop(10).Text(
                $"{CIUDAD}  a  {p.FechaSolicitud.Day} de {NombreMes(p.FechaSolicitud.Month)} del {p.FechaSolicitud.Year}")
                .FontSize(10);

            col.Item().PaddingTop(10).Table(t =>
            {
                t.ColumnsDefinition(c => { c.ConstantColumn(150); c.RelativeColumn(); });
                FilaDato(t, "A NOMBRE DE", p.Socio.Nombre);
                FilaDato(t, "LA CANTIDAD DE", NumeroALetras(p.ImporteOtorgado).ToUpper());
                FilaDato(t, "FOLIO", p.Folio ?? "");
            });

            // Concepto y firma
            col.Item().PaddingTop(12).Table(t =>
            {
                t.ColumnsDefinition(c => { c.RelativeColumn(); c.RelativeColumn(); });

                t.Cell().Border(0.5f).BorderColor(GRIS).Padding(10).Column(f =>
                {
                    f.Item().Text("CONCEPTO").Bold().FontSize(9).FontColor(AZUL);
                    f.Item().PaddingTop(6).Text("PRÉSTAMO DE CAJA DE AHORRO").FontSize(10);
                });
                t.Cell().Border(0.5f).BorderColor(GRIS).Padding(10).AlignCenter().Column(f =>
                {
                    f.Item().Text("FIRMA DE RECIBIDO").Bold().FontSize(9).FontColor(AZUL);
                    f.Item().PaddingTop(38).LineHorizontal(0.8f);
                });
            });

            // Asiento contable
            col.Item().PaddingTop(16).Text("ASIENTO CONTABLE").Bold().FontSize(9).FontColor(AZUL);
            col.Item().PaddingTop(4).Table(t =>
            {
                t.ColumnsDefinition(c => { c.RelativeColumn(4); c.ConstantColumn(110); c.ConstantColumn(110); });

                void Th(string s, bool center = false)
                {
                    var cell = t.Cell().Border(0.5f).BorderColor(GRIS).Background(AZUL).PaddingVertical(4).PaddingHorizontal(8);
                    (center ? cell.AlignCenter() : cell).Text(s).Bold().FontSize(9).FontColor("#FFFFFF");
                }
                Th("CONCEPTO"); Th("DEBE", true); Th("HABER", true);

                t.Cell().Border(0.5f).BorderColor(GRIS).Padding(6).Column(f =>
                {
                    f.Item().Text("PRÉSTAMOS A SOCIOS").Bold().FontSize(9);
                    f.Item().Text($"Préstamo a {p.NumeroDescuentos} quincenas").FontSize(8).FontColor(GRIS);
                });
                t.Cell().Border(0.5f).BorderColor(GRIS).Padding(6).AlignRight().Text($"$ {p.ImporteOtorgado:N2}").FontSize(9);
                t.Cell().Border(0.5f).BorderColor(GRIS).Padding(6).Text("");

                t.Cell().Border(0.5f).BorderColor(GRIS).Padding(6).Column(f =>
                {
                    f.Item().Text("BANCOS").Bold().FontSize(9);
                    f.Item().Text($"Cuenta No. {CUENTA} · Cheque {p.NumeroChequeFolio}").FontSize(8).FontColor(GRIS);
                });
                t.Cell().Border(0.5f).BorderColor(GRIS).Padding(6).Text("");
                t.Cell().Border(0.5f).BorderColor(GRIS).Padding(6).AlignRight().Text($"$ {p.ImporteOtorgado:N2}").FontSize(9);

                t.Cell().Border(0.5f).BorderColor(GRIS).Background(GRISCL).Padding(6).Text("SUMAS IGUALES").Bold().FontSize(9);
                t.Cell().Border(0.5f).BorderColor(GRIS).Background(GRISCL).Padding(6).AlignRight().Text($"$ {p.ImporteOtorgado:N2}").Bold().FontSize(9);
                t.Cell().Border(0.5f).BorderColor(GRIS).Background(GRISCL).Padding(6).AlignRight().Text($"$ {p.ImporteOtorgado:N2}").Bold().FontSize(9);
            });
        });
    }

    // ── 4. TABLA DE AMORTIZACIÓN ─────────────────────────────────────────────
    private void PaginaAmortizacion(PageDescriptor page, Prestamo p, List<AmortizacionDetalle> amortizacion)
    {
        page.Size(PageSizes.Letter);
        page.Margin(1.2f, Unit.Centimetre);   // antes 1.6 — se reduce para que quepa en 1 hoja
        page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Arial").FontColor("#1A1A2E"));

        var totalIntereses = p.TotalAPagar - p.ImporteOtorgado;
        var tasaTotalPct   = p.TasaInteres * (p.NumeroDescuentos + 2) * 100m;

        page.Content().Column(col =>
        {
            EncabezadoOficial(col, "TABLA DE AMORTIZACIÓN");

            // Datos del prestatario
            col.Item().PaddingTop(8).Table(t =>
            {
                t.ColumnsDefinition(c => { c.ConstantColumn(95); c.RelativeColumn(); c.ConstantColumn(95); c.RelativeColumn(); });
                void Lbl(string s) => t.Cell().Border(0.5f).BorderColor(GRIS).Background(GRISCL)
                    .PaddingVertical(2).PaddingHorizontal(6).Text(s).Bold().FontSize(8);
                void Val(string s) => t.Cell().Border(0.5f).BorderColor(GRIS)
                    .PaddingVertical(2).PaddingHorizontal(6).Text(s ?? "").FontSize(8);

                Lbl("NOMBRE");
                t.Cell().ColumnSpan(3).Border(0.5f).BorderColor(GRIS).PaddingVertical(2).PaddingHorizontal(6)
                    .Text(p.Socio.Nombre).Bold().FontSize(9);
                Lbl("R.F.C."); Val(p.Socio.RFC); Lbl("ADSCRIPCIÓN"); Val(p.Socio.Adscripcion);
                Lbl("NOMBRAMIENTO"); Val(p.Socio.TipoNombramiento); Lbl("FOLIO"); Val(p.Folio ?? "");
            });

            // Resumen del crédito (recuadro tipo formato del prestatario)
            col.Item().PaddingTop(6).Table(t =>
            {
                t.ColumnsDefinition(c =>
                { c.RelativeColumn(); c.RelativeColumn(); c.RelativeColumn(); });

                void Caja(string etiqueta, string valor, string color = "#1A1A2E")
                {
                    t.Cell().Border(0.5f).BorderColor(GRIS).Background(AZULTEN).Padding(5).Column(f =>
                    {
                        f.Item().Text(etiqueta).FontSize(7).FontColor(GRIS);
                        f.Item().Text(valor).Bold().FontSize(10).FontColor(color);
                    });
                }

                Caja("IMPORTE INICIAL", $"$ {p.ImporteOtorgado:N2}");
                Caja("TASA QUINCENAL", $"{p.TasaInteres * 100:N1}%");
                Caja("TASA TOTAL", $"{tasaTotalPct:N1}%");

                // El interés se calcula a NumeroDescuentos+2 quincenas (una de
                // gracia antes del primer descuento y una al final), pero el
                // socio solo PAGA NumeroDescuentos quincenas. Mostrar nada más
                // "PLAZO: 24 qnas" es ambiguo — se separan los dos números.
                Caja("PLAZO (INTERÉS)", $"{p.NumeroDescuentos + 2} qnas");
                Caja("PAGO A", $"{p.NumeroDescuentos} qnas");
                Caja("DESCUENTO QUINCENAL", $"$ {p.PagoQuincenal:N2}", AZUL);

                Caja("TOTAL INTERESES", $"$ {totalIntereses:N2}");
                Caja("TOTAL DEL PRÉSTAMO", $"$ {p.TotalAPagar:N2}", ROJO);
                Caja("QNAS", $"{p.QuincenaInicial:D2}/{p.AnioInicial} → {p.QuincenaFinal:D2}/{p.AnioFinal}");
            });

            // Tabla de pagos (modelo de interés plano: pago fijo + saldo)
            col.Item().PaddingTop(8).Table(t =>
            {
                t.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(32);   // #
                    c.RelativeColumn(2);    // Quincena
                    c.RelativeColumn(2);    // Pagos pendientes
                    c.RelativeColumn(2);    // Pago quincenal
                    c.RelativeColumn(2);    // Saldo
                });

                void H(string s) => t.Cell().Background(AZUL).Border(0.5f).BorderColor(AZUL).Padding(3)
                    .AlignCenter().Text(s).FontColor("#FFFFFF").Bold().FontSize(8);

                H("#"); H("QUINCENA"); H("PAGOS PEND."); H("PAGO QNA."); H("SALDO");

                int total = amortizacion.Count;
                bool alt = false;
                foreach (var d in amortizacion)
                {
                    var bg = alt ? "#F7F9FC" : "#FFFFFF";
                    void C(string s, bool right = false)
                    {
                        var cell = t.Cell().Background(bg).Border(0.5f).BorderColor("#E5E7EB").Padding(2);
                        (right ? cell.AlignRight() : cell.AlignCenter()).Text(s).FontSize(8);
                    }
                    C(d.NumeroPago.ToString());
                    C($"{d.Quincena:D2}/{d.Anio}");
                    C((total - d.NumeroPago).ToString());
                    C($"$ {d.Pago:N2}", true);
                    C($"$ {d.SaldoFinal:N2}", true);
                    alt = !alt;
                }

                // Totales
                t.Cell().ColumnSpan(3).Background("#D5E0F0").Border(0.5f).BorderColor(GRIS).Padding(3)
                    .Text("TOTALES").Bold().FontSize(8);
                t.Cell().Background("#D5E0F0").Border(0.5f).BorderColor(GRIS).Padding(3)
                    .AlignRight().Text($"$ {amortizacion.Sum(a => a.Pago):N2}").Bold().FontSize(8);
                t.Cell().Background("#D5E0F0").Border(0.5f).BorderColor(GRIS).Padding(3)
                    .AlignRight().Text("$ 0.00").Bold().FontSize(8);
            });

            // Firmas
            col.Item().PaddingTop(16).Table(t =>
            {
                t.ColumnsDefinition(c => { c.RelativeColumn(); c.ConstantColumn(60); c.RelativeColumn(); });
                t.Cell().AlignCenter().Column(f =>
                {
                    f.Item().LineHorizontal(0.8f);
                    f.Item().PaddingTop(2).AlignCenter().Text(p.Socio.Nombre).Bold().FontSize(8);
                    f.Item().AlignCenter().Text("FIRMA DEL TRABAJADOR").FontSize(7.5f).FontColor(GRIS);
                });
                t.Cell();
                t.Cell().AlignCenter().Column(f =>
                {
                    f.Item().LineHorizontal(0.8f);
                    f.Item().PaddingTop(2).AlignCenter().Text("CAJA DE AHORRO · SECCIÓN 39").Bold().FontSize(8);
                    f.Item().AlignCenter().Text("FIRMA DE AUTORIZACIÓN").FontSize(7.5f).FontColor(GRIS);
                });
            });
        });
    }

    // ── HELPERS ──────────────────────────────────────────────────────────────

    private static DateTime FechaPrimerDiaQuincena(int quincena, int anio)
    {
        int mes = ((quincena - 1) / 2) + 1;
        if (mes < 1) mes = 1; if (mes > 12) mes = 12;
        int dia = (quincena % 2 == 1) ? 1 : 16;
        return new DateTime(anio, mes, dia);
    }

    private static DateTime FechaUltimoDiaQuincena(int quincena, int anio)
    {
        int mes = ((quincena - 1) / 2) + 1;
        if (mes < 1) mes = 1; if (mes > 12) mes = 12;
        return quincena % 2 == 1
            ? new DateTime(anio, mes, 15)
            : new DateTime(anio, mes, DateTime.DaysInMonth(anio, mes));
    }

    private static string NombreMes(int mes) => mes switch
    {
        1 => "ENERO",    2 => "FEBRERO",   3 => "MARZO",
        4 => "ABRIL",    5 => "MAYO",      6 => "JUNIO",
        7 => "JULIO",    8 => "AGOSTO",    9 => "SEPTIEMBRE",
        10 => "OCTUBRE", 11 => "NOVIEMBRE", _ => "DICIEMBRE"
    };

    private static string NumeroALetras(decimal numero)
    {
        var entero   = (long)Math.Floor(numero);
        var centavos = (int)Math.Round((numero - entero) * 100);
        var letras   = EnteroALetras(entero);
        return centavos > 0
            ? $"{letras} PESOS {centavos:D2}/100 M.N."
            : $"{letras} PESOS 00/100 M.N.";
    }

    private static string EnteroALetras(long n)
    {
        if (n == 0) return "CERO";
        if (n < 0)  return "MENOS " + EnteroALetras(-n);

        string[] u = { "", "UN", "DOS", "TRES", "CUATRO", "CINCO", "SEIS", "SIETE", "OCHO", "NUEVE",
                        "DIEZ", "ONCE", "DOCE", "TRECE", "CATORCE", "QUINCE", "DIECISÉIS",
                        "DIECISIETE", "DIECIOCHO", "DIECINUEVE" };
        string[] d = { "", "", "VEINTE", "TREINTA", "CUARENTA", "CINCUENTA",
                        "SESENTA", "SETENTA", "OCHENTA", "NOVENTA" };
        string[] c = { "", "CIENTO", "DOSCIENTOS", "TRESCIENTOS", "CUATROCIENTOS", "QUINIENTOS",
                        "SEISCIENTOS", "SETECIENTOS", "OCHOCIENTOS", "NOVECIENTOS" };

        if (n < 20)      return u[n];
        if (n < 30)      return n == 20 ? "VEINTE" : "VEINTI" + u[n - 20].ToLower();
        if (n < 100)     return d[n / 10] + (n % 10 > 0 ? " Y " + u[n % 10] : "");
        if (n == 100)    return "CIEN";
        if (n < 1000)    return c[n / 100] + (n % 100 > 0 ? " " + EnteroALetras(n % 100) : "");
        if (n < 2000)    return "MIL" + (n % 1000 > 0 ? " " + EnteroALetras(n % 1000) : "");
        if (n < 1000000) return EnteroALetras(n / 1000) + " MIL" + (n % 1000 > 0 ? " " + EnteroALetras(n % 1000) : "");
        if (n < 2000000) return "UN MILLÓN" + (n % 1000000 > 0 ? " " + EnteroALetras(n % 1000000) : "");
        return EnteroALetras(n / 1000000) + " MILLONES" + (n % 1000000 > 0 ? " " + EnteroALetras(n % 1000000) : "");
    }
}
