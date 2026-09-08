// NSQ_PDF_PROGRAMA_V1_1
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;

namespace ERP.NSQuell.Controllers;

public partial class PlaneacionProgramaController
{
    private sealed class ProgramaPdfFila
    {
        public int ProgramaProduccionID { get; set; }
        public int? ParteID { get; set; }
        public int? MaquinaID { get; set; }
        public string MaquinaCodigo { get; set; } = string.Empty;
        public string MaquinaNombre { get; set; } = string.Empty;
        public int? MoldeID { get; set; }
        public string MoldeCodigo { get; set; } = string.Empty;
        public string NumeroParte { get; set; } = string.Empty;
        public string ReferenciaSAP { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public int CantidadProgramada { get; set; }
        public string CondicionProduccion { get; set; } = string.Empty;
        public DateTime? FechaInicioProgramada { get; set; }
        public DateTime? FechaFinProgramada { get; set; }
        public decimal? HorasProgramadas { get; set; }
        public TimeSpan? Cambio { get; set; }
        public TimeSpan? Arranque { get; set; }
        public int? ObjetivoHora { get; set; }
        public string Ciclo { get; set; } = string.Empty;
        public int? Cavidades { get; set; }
        public string Observaciones { get; set; } = string.Empty;
        public int EstatusID { get; set; }
        public int? EjecucionProduccionID { get; set; }
        public DateTime? FechaInicioReal { get; set; }
        public DateTime? FechaFinReal { get; set; }
        public bool EsContinuidad { get; set; }

        public bool EjecucionAbierta =>
            EjecucionProduccionID.HasValue &&
            FechaInicioReal.HasValue &&
            !FechaFinReal.HasValue;
    }

    private sealed class ProgramaPdfMaquina
    {
        public int? MaquinaID { get; set; }
        public string MaquinaCodigo { get; set; } = string.Empty;
        public string MaquinaNombre { get; set; } = string.Empty;
        public ProgramaPdfFila? Contexto { get; set; }
        public bool ContextoEsProduccionReal { get; set; }
        public List<ProgramaPdfFila> Cambios { get; set; } = new();
    }

    [HttpGet]
    public async Task<IActionResult> PdfPrograma(
        DateTime? fechaDesde,
        DateTime? fechaHasta,
        [FromServices] IWebHostEnvironment environment,
        bool incluirActual = true)
    {
        if (!fechaDesde.HasValue || !fechaHasta.HasValue)
            return BadRequest("Debes indicar Fecha desde y Fecha hasta.");

        var desde = fechaDesde.Value.Date;
        var hasta = fechaHasta.Value.Date;

        if (hasta < desde)
            return BadRequest("Fecha hasta no puede ser menor que Fecha desde.");

        if ((hasta - desde).TotalDays > 31)
            return BadRequest("El rango maximo permitido para el PDF Programa es de 32 dias.");

        var hastaExclusiva = hasta.AddDays(1);

        // Para reportes actuales/futuros, la cabecera de cada maquina usa la produccion real de hoy.
        // Para reportes completamente historicos, usa el programa que estaba vigente al inicio del rango.
        var reporteHistorico = hasta < DateTime.Today;
        var momentoContexto = reporteHistorico ? desde : DateTime.Now;
        var usarEjecucionActual = incluirActual && !reporteHistorico;

        var filas = await ConsultarProgramaPdfAsync(
            desde,
            hastaExclusiva,
            momentoContexto,
            incluirActual,
            usarEjecucionActual);

        var maquinas = ConstruirMaquinasPdf(
            filas,
            desde,
            hastaExclusiva,
            momentoContexto,
            incluirActual,
            usarEjecucionActual);

        ConfigurarLicenciaQuestPdf(environment);

        byte[]? logo = null;
        var webRoot = string.IsNullOrWhiteSpace(environment.WebRootPath)
            ? System.IO.Path.Combine(environment.ContentRootPath, "wwwroot")
            : environment.WebRootPath;
        var logoPath = System.IO.Path.Combine(webRoot, "Imagenes", "logo-quell.png");
        if (System.IO.File.Exists(logoPath))
            logo = await System.IO.File.ReadAllBytesAsync(logoPath);

        // NSQ_PDF_PROGRAMA_REVISADO_USUARIO_V1_4
        // LoginController guarda el nombre humano en NombreCompleto/NombreMostrar.
        // Se dejan fallbacks para sesiones antiguas sin forzar una consulta extra a BD.
        var nombreRevisor = HttpContext.Session.GetString("NombreCompleto");
        if (string.IsNullOrWhiteSpace(nombreRevisor))
            nombreRevisor = HttpContext.Session.GetString("NombreMostrar");
        if (string.IsNullOrWhiteSpace(nombreRevisor))
            nombreRevisor = User?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(nombreRevisor))
            nombreRevisor = HttpContext.Session.GetString("Username");

        nombreRevisor = string.IsNullOrWhiteSpace(nombreRevisor)
            ? "Usuario no identificado"
            : nombreRevisor.Trim();

        var pdf = CrearDocumentoProgramaPdf(
            maquinas,
            desde,
            hasta,
            logo,
            nombreRevisor).GeneratePdf();

        var fileName = $"Programa_Cambio_Moldes_{desde:yyyyMMdd}_{hasta:yyyyMMdd}.pdf";
        Response.Headers["Content-Disposition"] = $"inline; filename=\"{fileName}\"";
        return File(pdf, "application/pdf");
    }

    // NSQ_PDF_PROGRAMA_LICENSE_V1_3
    private void ConfigurarLicenciaQuestPdf(IWebHostEnvironment environment)
    {
        // En desarrollo local se permite Community para probar/evaluar el generador.
        // En cualquier otro ambiente la licencia debe declararse explicitamente.
        var licenciaConfigurada = _configuration["QuestPDF:License"];

        if (string.IsNullOrWhiteSpace(licenciaConfigurada))
        {
            if (string.Equals(
                environment.EnvironmentName,
                "Development",
                StringComparison.OrdinalIgnoreCase))
            {
                QuestPDF.Settings.License = LicenseType.Community;
                return;
            }

            throw new InvalidOperationException(
                "Falta configurar QuestPDF:License para este ambiente. Usa Community, Professional o Enterprise segun la licencia que corresponda a la entidad.");
        }

        var licencia = licenciaConfigurada.Trim().ToUpperInvariant();

        QuestPDF.Settings.License = licencia switch
        {
            "COMMUNITY" => LicenseType.Community,
            "PROFESSIONAL" => LicenseType.Professional,
            "ENTERPRISE" => LicenseType.Enterprise,
            _ => throw new InvalidOperationException(
                "Configuracion QuestPDF:License invalida. Usa Community, Professional o Enterprise.")
        };
    }
    private async Task<List<ProgramaPdfFila>> ConsultarProgramaPdfAsync(
        DateTime desde,
        DateTime hastaExclusiva,
        DateTime momentoContexto,
        bool incluirActual,
        bool usarEjecucionActual)
    {
        var lista = new List<ProgramaPdfFila>();

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();

        const string sql = @"
SELECT
    pp.ProgramaProduccionID,
    pp.ParteID,
    pp.MaquinaID,
    ISNULL(NULLIF(pp.MaquinaCodigo,N''),N'SIN MAQUINA') AS MaquinaCodigo,
    ISNULL(NULLIF(pp.MaquinaNombre,N''),ISNULL(NULLIF(pp.MaquinaCodigo,N''),N'Sin maquina')) AS MaquinaNombre,
    pp.MoldeID,
    ISNULL(pp.MoldeCodigo,N'') AS MoldeCodigo,
    ISNULL(pp.NumeroParte,N'') AS NumeroParte,
    ISNULL(NULLIF(pp.ReferenciaSAP,N''),ISNULL(pp.NumeroParte,N'')) AS ReferenciaSAP,
    ISNULL(pp.DesignacionDescripcionSAP,N'') AS DesignacionDescripcionSAP,
    ISNULL(pp.CantidadProgramada,0) AS CantidadProgramada,
    ISNULL(pp.CondicionProduccion,N'') AS CondicionProduccion,
    pp.FechaInicioProgramada,
    pp.FechaFinProgramada,
    pp.HorasProgramadas,
    pp.Cambio,
    pp.Arranque,
    pp.ObjetivoHora,
    pp.Ciclo,
    pp.Cavidades,
    ISNULL(pp.Observaciones,N'') AS Observaciones,
    ISNULL(pp.EstatusID,1) AS EstatusID,
    ej.EjecucionProduccionID,
    ej.FechaInicioReal,
    ej.FechaFinReal
FROM dbo.Planeacion_ProgramaProduccion pp
OUTER APPLY
(
    SELECT TOP(1)
        e.EjecucionProduccionID,
        e.FechaInicioReal,
        e.FechaFinReal
    FROM dbo.Produccion_Ejecucion e
    WHERE e.ProgramaProduccionID = pp.ProgramaProduccionID
      AND e.Activo = 1
    ORDER BY
        CASE WHEN e.FechaInicioReal IS NOT NULL AND e.FechaFinReal IS NULL THEN 0 ELSE 1 END,
        e.EjecucionProduccionID DESC
) ej
WHERE pp.Activo = 1
  AND pp.MaquinaID IS NOT NULL
  AND ISNULL(pp.EstatusID,1) <> 99
  AND
  (
      (
          pp.FechaInicioProgramada >= @Desde
          AND pp.FechaInicioProgramada < @HastaExclusiva
      )
      OR
      (
          @IncluirActual = 1
          AND pp.FechaInicioProgramada IS NOT NULL
          AND pp.FechaInicioProgramada <= @MomentoContexto
          AND ISNULL(pp.EstatusID,1) IN (1,2,3,4)
          AND (pp.FechaFinProgramada IS NULL OR pp.FechaFinProgramada > @MomentoContexto)
      )
      OR
      (
          @UsarEjecucionActual = 1
          AND ej.EjecucionProduccionID IS NOT NULL
          AND ej.FechaInicioReal IS NOT NULL
          AND ej.FechaFinReal IS NULL
      )
  )
ORDER BY
    pp.MaquinaCodigo,
    pp.FechaInicioProgramada,
    pp.SecuenciaMaquina,
    pp.ProgramaProduccionID;";

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@Desde", SqlDbType.DateTime).Value = desde;
        cmd.Parameters.Add("@HastaExclusiva", SqlDbType.DateTime).Value = hastaExclusiva;
        cmd.Parameters.Add("@MomentoContexto", SqlDbType.DateTime).Value = momentoContexto;
        cmd.Parameters.Add("@IncluirActual", SqlDbType.Bit).Value = incluirActual;
        cmd.Parameters.Add("@UsarEjecucionActual", SqlDbType.Bit).Value = usarEjecucionActual;

        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
        {
            lista.Add(new ProgramaPdfFila
            {
                ProgramaProduccionID = Convert.ToInt32(rd["ProgramaProduccionID"]),
                ParteID = rd["ParteID"] == DBNull.Value ? null : Convert.ToInt32(rd["ParteID"]),
                MaquinaID = rd["MaquinaID"] == DBNull.Value ? null : Convert.ToInt32(rd["MaquinaID"]),
                MaquinaCodigo = rd["MaquinaCodigo"] as string ?? "SIN MAQUINA",
                MaquinaNombre = rd["MaquinaNombre"] as string ?? string.Empty,
                MoldeID = rd["MoldeID"] == DBNull.Value ? null : Convert.ToInt32(rd["MoldeID"]),
                MoldeCodigo = rd["MoldeCodigo"] as string ?? string.Empty,
                NumeroParte = rd["NumeroParte"] as string ?? string.Empty,
                ReferenciaSAP = rd["ReferenciaSAP"] as string ?? string.Empty,
                Descripcion = rd["DesignacionDescripcionSAP"] as string ?? string.Empty,
                CantidadProgramada = Convert.ToInt32(rd["CantidadProgramada"]),
                CondicionProduccion = rd["CondicionProduccion"] as string ?? string.Empty,
                FechaInicioProgramada = rd["FechaInicioProgramada"] == DBNull.Value
                    ? null
                    : Convert.ToDateTime(rd["FechaInicioProgramada"]),
                FechaFinProgramada = rd["FechaFinProgramada"] == DBNull.Value
                    ? null
                    : Convert.ToDateTime(rd["FechaFinProgramada"]),
                HorasProgramadas = rd["HorasProgramadas"] == DBNull.Value
                    ? null
                    : Convert.ToDecimal(rd["HorasProgramadas"]),
                Cambio = rd["Cambio"] == DBNull.Value ? null : (TimeSpan)rd["Cambio"],
                Arranque = rd["Arranque"] == DBNull.Value ? null : (TimeSpan)rd["Arranque"],
                ObjetivoHora = rd["ObjetivoHora"] == DBNull.Value ? null : Convert.ToInt32(rd["ObjetivoHora"]),
                Ciclo = rd["Ciclo"] == DBNull.Value ? string.Empty : rd["Ciclo"].ToString() ?? string.Empty,
                Cavidades = rd["Cavidades"] == DBNull.Value ? null : Convert.ToInt32(rd["Cavidades"]),
                Observaciones = rd["Observaciones"] as string ?? string.Empty,
                EstatusID = Convert.ToInt32(rd["EstatusID"]),
                EjecucionProduccionID = rd["EjecucionProduccionID"] == DBNull.Value
                    ? null
                    : Convert.ToInt32(rd["EjecucionProduccionID"]),
                FechaInicioReal = rd["FechaInicioReal"] == DBNull.Value
                    ? null
                    : Convert.ToDateTime(rd["FechaInicioReal"]),
                FechaFinReal = rd["FechaFinReal"] == DBNull.Value
                    ? null
                    : Convert.ToDateTime(rd["FechaFinReal"])
            });
        }

        return lista;
    }

    private static List<ProgramaPdfMaquina> ConstruirMaquinasPdf(
        List<ProgramaPdfFila> filas,
        DateTime desde,
        DateTime hastaExclusiva,
        DateTime momentoContexto,
        bool incluirActual,
        bool usarEjecucionActual)
    {
        var resultado = new List<ProgramaPdfMaquina>();

        foreach (var grupo in filas
                     .Where(x => x.MaquinaID.HasValue)
                     .GroupBy(x => new { x.MaquinaID, x.MaquinaCodigo, x.MaquinaNombre })
                     .OrderBy(g => OrdenMaquina(g.Key.MaquinaCodigo))
                     .ThenBy(g => g.Key.MaquinaCodigo, StringComparer.OrdinalIgnoreCase))
        {
            var ordenadas = grupo
                .OrderBy(x => x.FechaInicioProgramada ?? DateTime.MaxValue)
                .ThenBy(x => x.ProgramaProduccionID)
                .ToList();

            ProgramaPdfFila? contexto = null;
            var contextoReal = false;

            if (incluirActual)
            {
                if (usarEjecucionActual)
                {
                    contexto = ordenadas
                        .Where(x => x.EjecucionAbierta)
                        .OrderByDescending(x => x.FechaInicioReal)
                        .ThenByDescending(x => x.ProgramaProduccionID)
                        .FirstOrDefault();

                    contextoReal = contexto != null;
                }

                contexto ??= ordenadas
                    .Where(x =>
                        x.FechaInicioProgramada.HasValue &&
                        x.FechaInicioProgramada.Value <= momentoContexto &&
                        (!x.FechaFinProgramada.HasValue || x.FechaFinProgramada.Value > momentoContexto))
                    .OrderByDescending(x => x.FechaInicioProgramada)
                    .ThenByDescending(x => x.ProgramaProduccionID)
                    .FirstOrDefault();
            }

            var cambios = ordenadas
                .Where(x =>
                    x.FechaInicioProgramada.HasValue &&
                    x.FechaInicioProgramada.Value >= desde &&
                    x.FechaInicioProgramada.Value < hastaExclusiva &&
                    x.ProgramaProduccionID != (contexto?.ProgramaProduccionID ?? 0))
                .OrderBy(x => x.FechaInicioProgramada)
                .ThenBy(x => x.ProgramaProduccionID)
                .ToList();

            ProgramaPdfFila? anterior = contexto;
            foreach (var cambio in cambios)
            {
                cambio.EsContinuidad = EsMismoProductoYMismoMolde(anterior, cambio);
                anterior = cambio;
            }

            if (contexto == null && cambios.Count == 0)
                continue;

            resultado.Add(new ProgramaPdfMaquina
            {
                MaquinaID = grupo.Key.MaquinaID,
                MaquinaCodigo = grupo.Key.MaquinaCodigo,
                MaquinaNombre = grupo.Key.MaquinaNombre,
                Contexto = contexto,
                ContextoEsProduccionReal = contextoReal,
                Cambios = cambios
            });
        }

        return resultado;
    }

    private static bool EsMismoProductoYMismoMolde(ProgramaPdfFila? anterior, ProgramaPdfFila actual)
    {
        if (anterior == null)
            return false;

        var mismaParte = anterior.ParteID.HasValue && actual.ParteID.HasValue
            ? anterior.ParteID.Value == actual.ParteID.Value
            : string.Equals(
                anterior.ReferenciaSAP.Trim(),
                actual.ReferenciaSAP.Trim(),
                StringComparison.OrdinalIgnoreCase);

        var mismoMolde = anterior.MoldeID.HasValue && actual.MoldeID.HasValue
            ? anterior.MoldeID.Value == actual.MoldeID.Value
            : string.Equals(
                anterior.MoldeCodigo.Trim(),
                actual.MoldeCodigo.Trim(),
                StringComparison.OrdinalIgnoreCase);

        var encadenado =
            anterior.FechaFinProgramada.HasValue &&
            actual.FechaInicioProgramada.HasValue &&
            Math.Abs((actual.FechaInicioProgramada.Value - anterior.FechaFinProgramada.Value).TotalHours) <= 2;

        return mismaParte && mismoMolde && encadenado;
    }

    private static int OrdenMaquina(string codigo)
    {
        if (string.IsNullOrWhiteSpace(codigo))
            return int.MaxValue;

        var digitos = new string(codigo.Where(char.IsDigit).ToArray());
        return int.TryParse(digitos, out var numero) ? numero : int.MaxValue;
    }

    private static IDocument CrearDocumentoProgramaPdf(
        List<ProgramaPdfMaquina> maquinas,
        DateTime desde,
        DateTime hasta,
        byte[]? logo,
        string nombreRevisor)
    {
        return Document.Create(document =>
        {
            document.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(18);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(6.4f).FontColor("#111111"));

                page.Header().Element(container =>
                    ComponerEncabezado(container, desde, hasta, logo));

                page.Content()
                    .PaddingTop(7)
                    .Column(column =>
                    {
                        column.Spacing(7);

                        if (maquinas.Count == 0)
                        {
                            column.Item()
                                .Border(1)
                                .BorderColor("#C9CDD2")
                                .Background("#F7F8FA")
                                .Padding(12)
                                .Text("No existen cambios de molde programados dentro del rango seleccionado.")
                                .FontSize(9)
                                .SemiBold();
                        }
                        else
                        {
                            foreach (var maquina in maquinas)
                            {
                                column.Item().Element(container =>
                                    ComponerMaquina(container, maquina, desde));
                            }
                        }

                        column.Item().PaddingTop(6).Column(firma =>
                        {
                            firma.Spacing(3);
                            firma.Item().Text("Programa revisado vs OF por:").FontSize(6.3f);
                            firma.Item()
                                .Width(190)
                                .BorderBottom(0.7f)
                                .BorderColor("#666666")
                                .PaddingBottom(2)
                                .Text(nombreRevisor)
                                .FontSize(6.3f)
                                .SemiBold();
                            firma.Item().Text("Nombre y Firma del Supervisor").FontSize(6.1f);
                        });
                    });

                page.Footer()
                    .PaddingTop(4)
                    .Row(row =>
                    {
                        row.RelativeItem()
                            .Text("GQ-F-PL01-08 | Ver.05")
                            .FontSize(5.5f)
                            .FontColor("#666666");

                        row.RelativeItem()
                            .AlignRight()
                            .Text(text =>
                            {
                                text.DefaultTextStyle(x => x.FontSize(5.5f).FontColor("#666666"));
                                text.Span("Pagina ");
                                text.CurrentPageNumber();
                                text.Span(" de ");
                                text.TotalPages();
                            });
                    });
            });
        });
    }

    private static void ComponerEncabezado(
        IContainer container,
        DateTime desde,
        DateTime hasta,
        byte[]? logo)
    {
        container.Column(column =>
        {
            column.Item()
                .Border(0.8f)
                .BorderColor("#1F1F1F")
                .Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(70);
                        columns.RelativeColumn();
                        columns.ConstantColumn(92);
                    });

                    table.Cell().RowSpan(2)
                        .BorderRight(0.6f)
                        .BorderBottom(0.6f)
                        .BorderColor("#333333")
                        .Padding(5)
                        .AlignCenter()
                        .AlignMiddle()
                        .Element(c =>
                        {
                            if (logo != null && logo.Length > 0)
                                c.Height(28).Image(logo).FitArea();
                            else
                                c.Text("NS QUELL").Bold().FontSize(9);
                        });

                    table.Cell()
                        .BorderRight(0.6f)
                        .BorderBottom(0.6f)
                        .BorderColor("#333333")
                        .PaddingVertical(4)
                        .AlignCenter()
                        .Text("PROGRAMA DE CAMBIO DE MOLDE DE OPERACIONES")
                        .Bold()
                        .FontSize(8.5f);

                    table.Cell()
                        .BorderBottom(0.6f)
                        .BorderColor("#333333")
                        .Padding(3)
                        .AlignCenter()
                        .Text("Ver.05")
                        .FontSize(6);

                    table.Cell()
                        .BorderRight(0.6f)
                        .BorderColor("#333333")
                        .PaddingVertical(3)
                        .AlignCenter()
                        .Text("GQ-F-PL01-08")
                        .Bold()
                        .FontSize(7.3f);

                    table.Cell()
                        .Padding(3)
                        .AlignCenter()
                        .Text("Fecha formato: 04.08.2025")
                        .FontSize(5.6f);
                });

            column.Item().PaddingTop(3).Row(row =>
            {
                row.RelativeItem().Text(text =>
                {
                    text.DefaultTextStyle(x => x.FontSize(6.2f));
                    text.Span("Rango de cambios: ").SemiBold();
                    text.Span(desde.ToString("dd/MM/yyyy"));
                    text.Span(" - ");
                    text.Span(hasta.ToString("dd/MM/yyyy"));
                });

                row.RelativeItem().AlignRight().Text(text =>
                {
                    text.DefaultTextStyle(x => x.FontSize(6.2f));
                    text.Span("Generado: ").SemiBold();
                    text.Span(DateTime.Now.ToString("dd/MM/yyyy HH:mm"));
                });
            });
        });
    }

    private static void ComponerMaquina(
        IContainer container,
        ProgramaPdfMaquina maquina,
        DateTime fechaBaseColor)
    {
        container.Column(column =>
        {
            column.Spacing(1.5f);

            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(96);
                    columns.RelativeColumn(2.2f);
                    columns.RelativeColumn(1.5f);
                    columns.ConstantColumn(48);
                    columns.ConstantColumn(38);
                    columns.ConstantColumn(58);
                    columns.RelativeColumn(1.6f);
                });

                table.Cell().RowSpan(2)
                    .Background("#1F4E78")
                    .Border(0.5f)
                    .BorderColor("#1A1A1A")
                    .Padding(4)
                    .AlignCenter()
                    .AlignMiddle()
                    .Column(c =>
                    {
                        c.Item().Text($"Maq {Texto(maquina.MaquinaCodigo)}")
                            .Bold().FontSize(8).FontColor(Colors.White);
                        c.Item().Text(TextoCiclo(maquina.Contexto ?? maquina.Cambios.FirstOrDefault()))
                            .FontSize(5.4f).FontColor("#DDEBF7");
                        c.Item().Text(maquina.ContextoEsProduccionReal ? "EN PRODUCCION" : "CONTEXTO")
                            .FontSize(5.2f).SemiBold().FontColor("#DDEBF7");
                    });

                EncabezadoActual(table, "Designacion/Descripcion SAP");
                EncabezadoActual(table, "REFERENCIA SAP");
                EncabezadoActual(table, "Cantidad");
                EncabezadoActual(table, "Horas P");
                EncabezadoActual(table, "Nº Molde");
                EncabezadoActual(table, "Notas");

                var actual = maquina.Contexto;
                CeldaActual(table, actual == null ? "Sin produccion actual registrada" : Texto(actual.Descripcion), true);
                CeldaActual(table, actual == null ? "-" : Texto(actual.ReferenciaSAP));
                CeldaActual(table, actual == null ? "-" : actual.CantidadProgramada.ToString("N0", CultureInfo.GetCultureInfo("es-MX")), false, true);
                CeldaActual(table, actual == null ? "-" : FormatoHoras(ResolverHoras(actual)), false, true);
                CeldaActual(table, actual == null ? "-" : Texto(actual.MoldeCodigo), false, true);
                CeldaActual(table, actual == null ? "-" : Texto(actual.Observaciones));
            });

            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(46);
                    columns.ConstantColumn(31);
                    columns.ConstantColumn(31);
                    columns.ConstantColumn(34);
                    columns.RelativeColumn(2.7f);
                    columns.RelativeColumn(1.75f);
                    columns.ConstantColumn(42);
                    columns.ConstantColumn(31);
                    columns.ConstantColumn(54);
                    columns.RelativeColumn(1.7f);
                });

                table.Header(header =>
                {
                    EncabezadoCambio(header, "Fecha");
                    EncabezadoCambio(header, "Condicion");
                    EncabezadoCambio(header, "Cambio");
                    EncabezadoCambio(header, "Arranque");
                    EncabezadoCambio(header, "Designacion/Descripcion SAP");
                    EncabezadoCambio(header, "REFERENCIA SAP");
                    EncabezadoCambio(header, "Cantidad");
                    EncabezadoCambio(header, "Horas P");
                    EncabezadoCambio(header, "Nº Molde");
                    EncabezadoCambio(header, "Notas");
                });

                if (maquina.Cambios.Count == 0)
                {
                    table.Cell().ColumnSpan(10)
                        .Border(0.45f)
                        .BorderColor("#A6A6A6")
                        .Padding(3)
                        .AlignCenter()
                        .Text("Sin cambios de molde dentro del rango seleccionado.")
                        .FontColor("#666666")
                        .FontSize(5.8f);
                }
                else
                {
                    foreach (var cambio in maquina.Cambios)
                    {
                        var colorFecha = ColorFecha(cambio.FechaInicioProgramada?.Date, fechaBaseColor);
                        CeldaCambio(table, cambio.FechaInicioProgramada?.ToString("dd/MM/yyyy") ?? "-", true, colorFecha);
                        CeldaCambio(table, NormalizarCondicion(cambio.CondicionProduccion), true);
                        CeldaCambio(table, FormatoHoraCambio(cambio), true);
                        CeldaCambio(table, FormatoHoraArranque(cambio), true);

                        var descripcion = cambio.EsContinuidad
                            ? "AUMENTO / " + Texto(cambio.Descripcion)
                            : Texto(cambio.Descripcion);

                        CeldaCambio(table, descripcion, false, cambio.EsContinuidad ? "#E2F0D9" : null);
                        CeldaCambio(table, Texto(cambio.ReferenciaSAP));
                        CeldaCambio(table, cambio.CantidadProgramada.ToString("N0", CultureInfo.GetCultureInfo("es-MX")), true);
                        CeldaCambio(table, FormatoHoras(ResolverHoras(cambio)), true);
                        CeldaCambio(table, Texto(cambio.MoldeCodigo), true);
                        CeldaCambio(table, Texto(cambio.Observaciones));
                    }
                }
            });
        });
    }

    private static void EncabezadoActual(TableDescriptor table, string texto)
    {
        table.Cell()
            .Background("#D9EAF7")
            .Border(0.5f)
            .BorderColor("#555555")
            .Padding(2)
            .AlignCenter()
            .AlignMiddle()
            .Text(texto)
            .SemiBold()
            .FontSize(5.2f);
    }

    private static void CeldaActual(
        TableDescriptor table,
        string texto,
        bool resaltar = false,
        bool centrar = false)
    {
        var cell = table.Cell()
            .Background(resaltar ? "#E2F0D9" : Colors.White)
            .Border(0.5f)
            .BorderColor("#777777")
            .Padding(2)
            .AlignMiddle();

        if (centrar)
            cell = cell.AlignCenter();

        cell.Text(texto).FontSize(5.7f);
    }

    private static void EncabezadoCambio(TableCellDescriptor header, string texto)
    {
        header.Cell()
            .Background("#D9EAF7")
            .Border(0.5f)
            .BorderColor("#555555")
            .Padding(1.8f)
            .AlignCenter()
            .AlignMiddle()
            .Text(texto)
            .SemiBold()
            .FontSize(5f);
    }

    private static void CeldaCambio(
        TableDescriptor table,
        string texto,
        bool centrar = false,
        string? fondo = null)
    {
        var cell = table.Cell()
            .Background(fondo ?? Colors.White)
            .Border(0.45f)
            .BorderColor("#777777")
            .Padding(1.8f)
            .AlignMiddle();

        if (centrar)
            cell = cell.AlignCenter();

        cell.Text(texto).FontSize(5.4f);
    }

    private static string Texto(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();

    private static string TextoCiclo(ProgramaPdfFila? fila)
    {
        if (fila == null)
            return "Sin programa base";

        if (!string.IsNullOrWhiteSpace(fila.Ciclo))
            return $"Ciclo: {fila.Ciclo.Trim()}";

        if (fila.ObjetivoHora.HasValue && fila.ObjetivoHora.Value > 0)
            return $"Obj/h: {fila.ObjetivoHora.Value:N0}";

        return "Ciclo: -";
    }

    private static string NormalizarCondicion(string? condicion)
    {
        if (string.IsNullOrWhiteSpace(condicion))
            return "-";

        var valor = condicion.Trim().ToUpperInvariant().Replace(" ", string.Empty);
        return valor switch
        {
            "TP" or "T.P" or "T.P." => "T.P",
            "IP" or "I.P" or "I.P." => "I.P",
            _ => condicion.Trim()
        };
    }

    private static string FormatoHoraCambio(ProgramaPdfFila fila)
    {
        if (fila.Cambio.HasValue)
            return FormatoHora(fila.Cambio.Value);

        return fila.FechaInicioProgramada.HasValue
            ? FormatoHora(fila.FechaInicioProgramada.Value.TimeOfDay)
            : "-";
    }

    private static string FormatoHoraArranque(ProgramaPdfFila fila)
    {
        if (fila.Arranque.HasValue)
            return FormatoHora(fila.Arranque.Value);

        if (fila.FechaInicioProgramada.HasValue)
            return FormatoHora(fila.FechaInicioProgramada.Value.AddHours(1).TimeOfDay);

        return "-";
    }

    private static string FormatoHora(TimeSpan value)
    {
        var normalizada = TimeSpan.FromMinutes(
            Math.Round(value.TotalMinutes % (24 * 60)));

        if (normalizada < TimeSpan.Zero)
            normalizada = normalizada.Add(TimeSpan.FromDays(1));

        // NSQ_PDF_PROGRAMA_HORA_24H_V1_5
        // Siempre mostrar reloj de 24 horas completo: 08:00, 14:00, 22:30, etc.
        return $"{normalizada.Hours:00}:{normalizada.Minutes:00}";
    }

    private static decimal ResolverHoras(ProgramaPdfFila fila)
    {
        if (fila.HorasProgramadas.HasValue && fila.HorasProgramadas.Value > 0)
            return fila.HorasProgramadas.Value;

        if (fila.CantidadProgramada <= 0)
            return 0;

        if (fila.ObjetivoHora.HasValue && fila.ObjetivoHora.Value > 0)
            return Math.Ceiling(fila.CantidadProgramada / (decimal)fila.ObjetivoHora.Value);

        var ciclo = ParsearCicloSegundos(fila.Ciclo);
        if (ciclo.HasValue && ciclo.Value > 0 && fila.Cavidades.HasValue && fila.Cavidades.Value > 0)
        {
            // Replica la intencion del auxiliar Excel: RATE = 3600 / CICLO * CAVIDADES.
            var rate = 3600m / ciclo.Value * fila.Cavidades.Value;
            if (rate > 0)
                return Math.Ceiling(fila.CantidadProgramada / rate);
        }

        return 0;
    }

    private static decimal? ParsearCicloSegundos(string? ciclo)
    {
        if (string.IsNullOrWhiteSpace(ciclo))
            return null;

        var texto = ciclo.Trim().Replace(',', '.');
        if (decimal.TryParse(
                texto,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var numero) && numero > 0)
            return numero;

        if (TimeSpan.TryParse(ciclo, CultureInfo.InvariantCulture, out var tiempo) && tiempo.TotalSeconds > 0)
            return (decimal)tiempo.TotalSeconds;

        return null;
    }

    private static string FormatoHoras(decimal horas)
    {
        if (horas <= 0)
            return "-";

        var redondeadas = Math.Ceiling(horas);
        return redondeadas.ToString("0", CultureInfo.InvariantCulture);
    }

    private static string ColorFecha(DateTime? fecha, DateTime baseDate)
    {
        if (!fecha.HasValue)
            return "#FFFFFF";

        var indice = Math.Abs((fecha.Value.Date - baseDate.Date).Days) % 4;
        return indice switch
        {
            0 => "#FFF59D",
            1 => "#F8BBD0",
            2 => "#B3E5FC",
            _ => "#C8E6C9"
        };
    }
}