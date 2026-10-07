
﻿// NSQ_RRHH_EDITOR_COMPACTO_PDF_MODAL_V2_5
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Data;

namespace ERP.NSQuell.Controllers;

public partial class EscalaPersonalController
{
    private sealed class EscalaPdfHeaderV20
    {
        public int EscalaID { get; set; }
        public string Folio { get; set; } = string.Empty;
        public string CodigoDocumento { get; set; } = string.Empty;
        public string VersionDocumento { get; set; } = string.Empty;
        public DateTime FechaElaboracion { get; set; }
        public int Anio { get; set; }
        public int NumeroSemana { get; set; }
        public DateTime FechaInicio { get; set; }
        public DateTime FechaFin { get; set; }
        public string PeriodoTrabajo { get; set; } = string.Empty;
    }

    private sealed class EscalaPdfTurnoV20
    {
        public int EscalaTurnoID { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string Horario { get; set; } = string.Empty;
        public int Orden { get; set; }
    }

    private sealed class EscalaPdfAsignacionV20
    {
        public int PersonalID { get; set; }
        public string NumeroControl { get; set; } = string.Empty;
        public string Persona { get; set; } = string.Empty;
        public int FuncionID { get; set; }
        public string Funcion { get; set; } = string.Empty;
        public string Area { get; set; } = string.Empty;
        public int? MaquinaID { get; set; }
        public string Maquina { get; set; } = string.Empty;
        public int EscalaTurnoID { get; set; }
    }

    private sealed class EscalaPdfNovedadV20
    {
        public string Tipo { get; set; } = string.Empty;
        public string NumeroControl { get; set; } = string.Empty;
        public string Persona { get; set; } = string.Empty;
        public DateTime FechaInicio { get; set; }
        public DateTime? FechaFin { get; set; }
        public string Motivo { get; set; } = string.Empty;
    }

    [HttpGet]
    public async Task<IActionResult> DescargarPdfV20(
        int id,
        [FromServices] IWebHostEnvironment environment)
    {
        if (id <= 0)
            return BadRequest("Escala inválida.");

        var generado = await GenerarPdfEscalaV21Async(id,environment);
        if (!generado.HasValue)
            return NotFound();

        Response.Headers["Content-Disposition"] =
            $"attachment; filename=\"{generado.Value.FileName}\"";
        Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";

        return File(generado.Value.Bytes,"application/pdf");
    }

    [HttpGet]
    public async Task<IActionResult> VistaPdfV21(
        int id,
        [FromServices] IWebHostEnvironment environment)
    {
        if (id <= 0)
            return BadRequest("Escala inválida.");

        var generado = await GenerarPdfEscalaV21Async(id,environment);
        if (!generado.HasValue)
            return NotFound();

        Response.Headers["Content-Disposition"] =
            $"inline; filename=\"{generado.Value.FileName}\"";
        Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";

        return File(generado.Value.Bytes,"application/pdf");
    }

    private async Task<(byte[] Bytes,string FileName)?> GenerarPdfEscalaV21Async(
        int escalaID,
        IWebHostEnvironment environment)
    {
        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync();

        var header = await CargarHeaderPdfV20Async(escalaID,cn);
        if (header == null)
            return null;

        var turnos = await CargarTurnosPdfV20Async(escalaID,cn);
        var asignaciones = await CargarAsignacionesPdfV20Async(escalaID,cn);
        var novedades = await CargarNovedadesPdfV20Async(escalaID,cn);

        QuestPDF.Settings.License = LicenseType.Community;

        byte[]? logo = null;
        var webRoot = string.IsNullOrWhiteSpace(environment.WebRootPath)
            ? Path.Combine(environment.ContentRootPath,"wwwroot")
            : environment.WebRootPath;

        var logoPath = Path.Combine(webRoot,"Imagenes","logo-quell.png");
        if (System.IO.File.Exists(logoPath))
            logo = await System.IO.File.ReadAllBytesAsync(logoPath);

        var document = CrearEscalaPdfV21(
            header,
            turnos,
            asignaciones,
            novedades,
            logo);

        var bytes = document.GeneratePdf();
        var fileName = $"Escala_Semana_{header.NumeroSemana}_{header.Anio}.pdf";
        return (bytes,fileName);
    }

    private static async Task<EscalaPdfHeaderV20?> CargarHeaderPdfV20Async(
        int escalaID,
        SqlConnection cn)
    {
        const string sql = @"
SELECT
    EscalaID,Folio,CodigoDocumento,VersionDocumento,FechaElaboracion,
    Anio,NumeroSemana,FechaInicio,FechaFin,PeriodoTrabajo
FROM dbo.RRHH_EscalasPersonal
WHERE EscalaID=@EscalaID
  AND Activo=1;";

        await using var cmd = new SqlCommand(sql,cn);
        cmd.Parameters.Add("@EscalaID",SqlDbType.Int).Value = escalaID;

        await using var rd = await cmd.ExecuteReaderAsync();
        if (!await rd.ReadAsync())
            return null;

        return new EscalaPdfHeaderV20
        {
            EscalaID = Convert.ToInt32(rd["EscalaID"]),
            Folio = rd["Folio"]?.ToString()?.Trim() ?? string.Empty,
            CodigoDocumento = rd["CodigoDocumento"]?.ToString()?.Trim() ?? string.Empty,
            VersionDocumento = rd["VersionDocumento"]?.ToString()?.Trim() ?? string.Empty,
            FechaElaboracion = Convert.ToDateTime(rd["FechaElaboracion"]),
            Anio = Convert.ToInt32(rd["Anio"]),
            NumeroSemana = Convert.ToInt32(rd["NumeroSemana"]),
            FechaInicio = Convert.ToDateTime(rd["FechaInicio"]),
            FechaFin = Convert.ToDateTime(rd["FechaFin"]),
            PeriodoTrabajo = rd["PeriodoTrabajo"]?.ToString()?.Trim() ?? string.Empty
        };
    }

    private static async Task<List<EscalaPdfTurnoV20>> CargarTurnosPdfV20Async(
        int escalaID,
        SqlConnection cn)
    {
        var lista = new List<EscalaPdfTurnoV20>();

        const string sql = @"
SELECT EscalaTurnoID,Nombre,HoraInicio,HoraFin,EsFlexible,Orden
FROM dbo.RRHH_EscalaTurnos
WHERE EscalaID=@EscalaID
  AND Activo=1
ORDER BY Orden,EscalaTurnoID;";

        await using var cmd = new SqlCommand(sql,cn);
        cmd.Parameters.Add("@EscalaID",SqlDbType.Int).Value = escalaID;

        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
        {
            var flexible = Convert.ToBoolean(rd["EsFlexible"]);
            var inicio = rd["HoraInicio"] == DBNull.Value
                ? null
                : (TimeSpan?)rd["HoraInicio"];
            var fin = rd["HoraFin"] == DBNull.Value
                ? null
                : (TimeSpan?)rd["HoraFin"];

            var horario = flexible || !inicio.HasValue || !fin.HasValue
                ? rd["Nombre"]?.ToString()?.Trim() ?? string.Empty
                : $"{inicio.Value:hh\\:mm} a {fin.Value:hh\\:mm}";

            lista.Add(new EscalaPdfTurnoV20
            {
                EscalaTurnoID = Convert.ToInt32(rd["EscalaTurnoID"]),
                Nombre = rd["Nombre"]?.ToString()?.Trim() ?? string.Empty,
                Horario = horario,
                Orden = Convert.ToInt32(rd["Orden"])
            });
        }

        return lista;
    }

    private static async Task<List<EscalaPdfAsignacionV20>> CargarAsignacionesPdfV20Async(
        int escalaID,
        SqlConnection cn)
    {
        var lista = new List<EscalaPdfAsignacionV20>();

        const string sql = @"
SELECT
    a.PersonalID,
    ISNULL(p.NumeroControl,N'') AS NumeroControl,
    LTRIM(RTRIM(
        ISNULL(p.Nombre,N'')+N' '+
        ISNULL(p.ApellidoPaterno,N'')+N' '+
        ISNULL(p.ApellidoMaterno,N'')
    )) AS Persona,
    a.FuncionID,
    ISNULL(f.Nombre,N'SIN FUNCION') AS Funcion,
    ISNULL(NULLIF(d.NombreDepartamento,N''),N'GENERAL') AS Area,
    a.MaquinaID,
    CASE
        WHEN a.MaquinaID IS NULL THEN N''
        ELSE COALESCE(
            NULLIF(LTRIM(RTRIM(m.Codigo)),N''),
            NULLIF(LTRIM(RTRIM(m.Nombre)),N''),
            CONVERT(nvarchar(20),a.MaquinaID)
        )
    END AS Maquina,
    a.EscalaTurnoID
FROM dbo.RRHH_EscalaAsignaciones a
INNER JOIN dbo.Persona p
    ON p.PersonaID=a.PersonalID
LEFT JOIN dbo.RRHH_FuncionesPersonal f
    ON f.FuncionID=a.FuncionID
LEFT JOIN dbo.Departamentos d
    ON d.DepartamentoID=f.DepartamentoID
LEFT JOIN dbo.ERP_Maquinas m
    ON m.MaquinaID=a.MaquinaID
WHERE a.EscalaID=@EscalaID
  AND a.Activo=1
ORDER BY
    ISNULL(d.NombreDepartamento,N'GENERAL'),
    ISNULL(f.Orden,9999),
    f.Nombre,
    m.Codigo,
    p.Nombre,
    p.ApellidoPaterno;";

        await using var cmd = new SqlCommand(sql,cn);
        cmd.Parameters.Add("@EscalaID",SqlDbType.Int).Value = escalaID;

        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
        {
            lista.Add(new EscalaPdfAsignacionV20
            {
                PersonalID = Convert.ToInt32(rd["PersonalID"]),
                NumeroControl = rd["NumeroControl"]?.ToString()?.Trim() ?? string.Empty,
                Persona = rd["Persona"]?.ToString()?.Trim() ?? string.Empty,
                FuncionID = Convert.ToInt32(rd["FuncionID"]),
                Funcion = rd["Funcion"]?.ToString()?.Trim() ?? string.Empty,
                Area = rd["Area"]?.ToString()?.Trim() ?? "GENERAL",
                MaquinaID = rd["MaquinaID"] == DBNull.Value
                    ? null
                    : Convert.ToInt32(rd["MaquinaID"]),
                Maquina = rd["Maquina"]?.ToString()?.Trim() ?? string.Empty,
                EscalaTurnoID = Convert.ToInt32(rd["EscalaTurnoID"])
            });
        }

        return lista;
    }

    private static async Task<List<EscalaPdfNovedadV20>> CargarNovedadesPdfV20Async(
        int escalaID,
        SqlConnection cn)
    {
        var lista = new List<EscalaPdfNovedadV20>();

        const string sql = @"
SELECT
    n.TipoNovedad,
    ISNULL(p.NumeroControl,N'') AS NumeroControl,
    LTRIM(RTRIM(
        ISNULL(p.Nombre,N'')+N' '+
        ISNULL(p.ApellidoPaterno,N'')+N' '+
        ISNULL(p.ApellidoMaterno,N'')
    )) AS Persona,
    n.FechaInicio,
    n.FechaFin,
    ISNULL(NULLIF(n.Motivo,N''),ISNULL(n.Observaciones,N'')) AS Motivo
FROM dbo.RRHH_NovedadesPersonal n
INNER JOIN dbo.Persona p
    ON p.PersonaID=n.PersonalID
WHERE n.EscalaID=@EscalaID
  AND n.Activo=1
ORDER BY n.TipoNovedad,n.FechaInicio,p.Nombre,p.ApellidoPaterno;";

        await using var cmd = new SqlCommand(sql,cn);
        cmd.Parameters.Add("@EscalaID",SqlDbType.Int).Value = escalaID;

        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
        {
            lista.Add(new EscalaPdfNovedadV20
            {
                Tipo = rd["TipoNovedad"]?.ToString()?.Trim() ?? string.Empty,
                NumeroControl = rd["NumeroControl"]?.ToString()?.Trim() ?? string.Empty,
                Persona = rd["Persona"]?.ToString()?.Trim() ?? string.Empty,
                FechaInicio = Convert.ToDateTime(rd["FechaInicio"]),
                FechaFin = rd["FechaFin"] == DBNull.Value
                    ? null
                    : Convert.ToDateTime(rd["FechaFin"]),
                Motivo = rd["Motivo"]?.ToString()?.Trim() ?? string.Empty
            });
        }

        return lista;
    }

    // NSQ_RRHH_PDF_DISTRIBUCION_LIMPIA_V2_6
    // Conserva la vista previa en modal de V2_5.
    // Cambia UNICAMENTE la composicion visual del PDF:
    // - recupera el encabezado documental clasico,
    // - elimina el banner azul tipo dashboard,
    // - distribuye el personal por funcion y por turno en bloques legibles,
    // - cada persona ocupa su propia linea con No. de control y maquina preferente.

    private static IDocument CrearEscalaPdfV21(
        EscalaPdfHeaderV20 header,
        List<EscalaPdfTurnoV20> turnos,
        List<EscalaPdfAsignacionV20> asignaciones,
        List<EscalaPdfNovedadV20> novedades,
        byte[]? logo)
    {
        var turnosOrdenados = turnos
            .OrderBy(x => x.Orden)
            .ThenBy(x => x.Nombre)
            .ToList();

        var areas = asignaciones
            .GroupBy(x =>
                string.IsNullOrWhiteSpace(x.Area)
                    ? "GENERAL"
                    : x.Area.Trim())
            .OrderBy(x => OrdenAreaPdfV21(x.Key))
            .ThenBy(x => x.Key)
            .ToList();

        return Document.Create(document =>
        {
            document.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(12);
                page.PageColor(Colors.White);

                page.DefaultTextStyle(x =>
                    x.FontSize(6.4f)
                     .FontColor("#24364D"));

                page.Content().Column(column =>
                {
                    column.Spacing(4);

                    // -------------------------------------------------
                    // Encabezado documental clasico.
                    // -------------------------------------------------
                    column.Item()
                        .Border(0.8f)
                        .BorderColor("#374151")
                        .Row(row =>
                        {
                            row.ConstantItem(82)
                                .Height(47)
                                .AlignCenter()
                                .AlignMiddle()
                                .Padding(5)
                                .Element(c =>
                                {
                                    if (logo != null && logo.Length > 0)
                                        c.Image(logo).FitArea();
                                    else
                                        c.Text("NS QUELL")
                                            .Bold()
                                            .FontSize(8)
                                            .FontColor("#17365F");
                                });

                            row.RelativeItem()
                                .BorderLeft(0.7f)
                                .BorderRight(0.7f)
                                .BorderColor("#374151")
                                .Column(c =>
                                {
                                    c.Item()
                                        .Height(26)
                                        .AlignCenter()
                                        .AlignMiddle()
                                        .Text("ESCALA DE PERSONAL SEMANAL")
                                        .Bold()
                                        .FontSize(12)
                                        .FontColor("#17233B");

                                    c.Item()
                                        .BorderTop(0.55f)
                                        .BorderColor("#374151")
                                        .Height(21)
                                        .AlignCenter()
                                        .AlignMiddle()
                                        .Text(
                                            string.IsNullOrWhiteSpace(header.CodigoDocumento)
                                                ? "GQ-F-PR01-10"
                                                : header.CodigoDocumento)
                                        .Bold()
                                        .FontSize(7)
                                        .FontColor("#233B59");
                                });

                            row.ConstantItem(110)
                                .Column(c =>
                                {
                                    c.Item()
                                        .Height(23.5f)
                                        .AlignCenter()
                                        .AlignMiddle()
                                        .Text($"Ver. {header.VersionDocumento}")
                                        .FontSize(6.4f);

                                    c.Item()
                                        .BorderTop(0.55f)
                                        .BorderColor("#374151")
                                        .Height(23.5f)
                                        .AlignCenter()
                                        .AlignMiddle()
                                        .Text($"Fecha: {header.FechaElaboracion:dd/MM/yyyy}")
                                        .FontSize(6.1f);
                                });
                        });

                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(92);
                            columns.RelativeColumn();
                            columns.ConstantColumn(72);
                            columns.ConstantColumn(72);
                        });

                        PdfMetaHeaderCellV26(table,"Área");
                        PdfMetaHeaderCellV26(table,"Periodo");
                        PdfMetaHeaderCellV26(table,"Semana");
                        PdfMetaHeaderCellV26(table,"Año");

                        PdfMetaValueCellV26(table,"Personal");
                        PdfMetaValueCellV26(
                            table,
                            $"{header.FechaInicio:dd/MM/yyyy} - {header.FechaFin:dd/MM/yyyy}");
                        PdfMetaValueCellV26(table,header.NumeroSemana.ToString());
                        PdfMetaValueCellV26(table,header.Anio.ToString());
                    });

                    // -------------------------------------------------
                    // Personal.
                    // Cada funcion tiene tarjetas por horario.
                    // Ya no se concatenan todos los operadores dentro
                    // de una sola celda angosta.
                    // -------------------------------------------------
                    if (areas.Count == 0)
                    {
                        column.Item()
                            .Border(0.6f)
                            .BorderColor("#CBD5E1")
                            .Padding(12)
                            .AlignCenter()
                            .Text("Todavía no hay personal asignado a esta escala.")
                            .FontColor("#64748B");
                    }
                    else
                    {
                        foreach (var area in areas)
                        {
                            column.Item()
                                .PaddingTop(2)
                                .Background("#13A4D8")
                                .PaddingVertical(3.5f)
                                .PaddingHorizontal(7)
                                .Text(area.Key.ToUpperInvariant())
                                .Bold()
                                .FontSize(7.5f)
                                .FontColor(Colors.White);

                            var funciones = area
                                .GroupBy(x => new
                                {
                                    x.FuncionID,
                                    Funcion = string.IsNullOrWhiteSpace(x.Funcion)
                                        ? "SIN FUNCIÓN"
                                        : x.Funcion.Trim()
                                })
                                .OrderBy(x => OrdenFuncionPdfV26(x.Key.Funcion))
                                .ThenBy(x => x.Key.Funcion)
                                .ToList();

                            foreach (var funcion in funciones)
                            {
                                var asignacionesFuncion = funcion
                                    .OrderBy(x => x.Persona)
                                    .ToList();

                                column.Item()
                                    .Border(0.55f)
                                    .BorderColor("#CBD5E1")
                                    .Column(block =>
                                    {
                                        block.Item()
                                            .Background("#EEF4FA")
                                            .PaddingVertical(3)
                                            .PaddingHorizontal(6)
                                            .Row(row =>
                                            {
                                                row.RelativeItem()
                                                    .Text(funcion.Key.Funcion.ToUpperInvariant())
                                                    .Bold()
                                                    .FontSize(6.8f)
                                                    .FontColor("#17365F");

                                                row.ConstantItem(110)
                                                    .AlignRight()
                                                    .Text($"{asignacionesFuncion.Select(x => x.PersonalID).Distinct().Count()} persona(s)")
                                                    .FontSize(5.6f)
                                                    .FontColor("#64748B");
                                            });

                                        for (var indice = 0; indice < turnosOrdenados.Count; indice += 3)
                                        {
                                            var lote = turnosOrdenados
                                                .Skip(indice)
                                                .Take(3)
                                                .ToList();

                                            block.Item()
                                                .Padding(3)
                                                .Row(row =>
                                                {
                                                    for (var pos = 0; pos < 3; pos++)
                                                    {
                                                        if (pos > 0)
                                                            row.ConstantItem(4);

                                                        if (pos >= lote.Count)
                                                        {
                                                            row.RelativeItem().Text(string.Empty);
                                                            continue;
                                                        }

                                                        var turno = lote[pos];
                                                        var personas = asignacionesFuncion
                                                            .Where(x => x.EscalaTurnoID == turno.EscalaTurnoID)
                                                            .OrderBy(x => x.Persona)
                                                            .ToList();

                                                        row.RelativeItem()
                                                            .Element(c =>
                                                                PdfTurnoCardV26(
                                                                    c,
                                                                    turno,
                                                                    personas));
                                                    }
                                                });
                                        }
                                    });
                            }
                        }
                    }

                    // -------------------------------------------------
                    // Novedades semanales.
                    // -------------------------------------------------
                    column.Item()
                        .PaddingTop(2)
                        .Text("NOVEDADES DE LA SEMANA")
                        .Bold()
                        .FontSize(7.2f)
                        .FontColor("#17365F");

                    if (novedades.Count == 0)
                    {
                        column.Item()
                            .Border(0.55f)
                            .BorderColor("#D7E0EB")
                            .Padding(5)
                            .Text("Sin novedades registradas.")
                            .FontColor("#718096");
                    }
                    else
                    {
                        column.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.ConstantColumn(82);
                                columns.RelativeColumn(1.35f);
                                columns.ConstantColumn(84);
                                columns.RelativeColumn(1.5f);
                            });

                            PdfHeaderCellV26(table,"TIPO");
                            PdfHeaderCellV26(table,"PERSONA");
                            PdfHeaderCellV26(table,"FECHAS");
                            PdfHeaderCellV26(table,"DETALLE");

                            foreach (var novedad in novedades
                                         .OrderBy(x => OrdenNovedadPdfV21(x.Tipo))
                                         .ThenBy(x => x.FechaInicio)
                                         .ThenBy(x => x.Persona))
                            {
                                var persona =
                                    string.IsNullOrWhiteSpace(novedad.NumeroControl)
                                        ? novedad.Persona
                                        : $"{novedad.NumeroControl} · {novedad.Persona}";

                                var fechas = novedad.FechaFin.HasValue
                                    ? $"{novedad.FechaInicio:dd/MM} - {novedad.FechaFin:dd/MM}"
                                    : $"{novedad.FechaInicio:dd/MM}";

                                PdfBodyCellV26(
                                    table,
                                    novedad.Tipo.ToUpperInvariant(),
                                    true);
                                PdfBodyCellV26(table,persona,false);
                                PdfBodyCellV26(table,fechas,false);
                                PdfBodyCellV26(table,novedad.Motivo,false);
                            }
                        });
                    }
                });

                page.Footer()
                    .PaddingTop(3)
                    .BorderTop(0.45f)
                    .BorderColor("#D8E0EA")
                    .Row(row =>
                    {
                        row.RelativeItem()
                            .Text(
                                $"{header.Folio} · " +
                                $"{header.CodigoDocumento} · " +
                                $"Semana {header.NumeroSemana}")
                            .FontSize(5.2f)
                            .FontColor("#667085");

                        row.RelativeItem()
                            .AlignRight()
                            .Text(text =>
                            {
                                text.DefaultTextStyle(x =>
                                    x.FontSize(5.2f)
                                     .FontColor("#667085"));

                                text.Span("Página ");
                                text.CurrentPageNumber();
                                text.Span(" de ");
                                text.TotalPages();
                            });
                    });
            });
        });
    }

    private static void PdfTurnoCardV26(
        IContainer container,
        EscalaPdfTurnoV20 turno,
        List<EscalaPdfAsignacionV20> personas)
    {
        container
            .Border(0.5f)
            .BorderColor("#B8C5D6")
            .Column(card =>
            {
                card.Item()
                    .Background("#17365F")
                    .PaddingVertical(3)
                    .PaddingHorizontal(5)
                    .Row(row =>
                    {
                        row.RelativeItem()
                            .Text(turno.Nombre)
                            .Bold()
                            .FontSize(6.1f)
                            .FontColor(Colors.White);

                        row.RelativeItem()
                            .AlignRight()
                            .Text(turno.Horario)
                            .FontSize(5.4f)
                            .FontColor("#DCE7F5");
                    });

                if (personas.Count == 0)
                {
                    card.Item()
                        .PaddingVertical(5)
                        .PaddingHorizontal(4)
                        .AlignCenter()
                        .Text("Sin personal")
                        .FontSize(5.5f)
                        .FontColor("#94A3B8");

                    return;
                }

                foreach (var persona in personas)
                {
                    card.Item()
                        .BorderBottom(0.35f)
                        .BorderColor("#E2E8F0")
                        .PaddingVertical(2.3f)
                        .PaddingHorizontal(4)
                        .Row(row =>
                        {
                            row.ConstantItem(34)
                                .AlignMiddle()
                                .Text(
                                    string.IsNullOrWhiteSpace(persona.NumeroControl)
                                        ? "—"
                                        : persona.NumeroControl)
                                .Bold()
                                .FontSize(5.6f)
                                .FontColor("#17365F");

                            row.RelativeItem()
                                .PaddingHorizontal(2)
                                .AlignMiddle()
                                .Text(persona.Persona)
                                .FontSize(5.6f)
                                .FontColor("#263A52");

                            row.ConstantItem(60)
                                .AlignRight()
                                .AlignMiddle()
                                .Text(
                                    string.IsNullOrWhiteSpace(persona.Maquina)
                                        ? string.Empty
                                        : persona.Maquina)
                                .FontSize(5.0f)
                                .FontColor("#64748B");
                        });
                }
            });
    }

    private static int OrdenAreaPdfV21(string area)
    {
        var valor = (area ?? string.Empty).Trim().ToUpperInvariant();

        if (valor.Contains("PRODUC")) return 10;
        if (valor.Contains("MANTEN")) return 20;
        if (valor.Contains("CALIDAD")) return 30;
        if (valor == "SGC" || valor.Contains("GESTION")) return 40;
        if (valor.Contains("LOGIST")) return 50;
        if (valor.Contains("TOOL")) return 60;

        return 90;
    }

    private static int OrdenFuncionPdfV26(string funcion)
    {
        var valor = (funcion ?? string.Empty)
            .Trim()
            .ToUpperInvariant();

        if (valor.Contains("RESPONSABLE") && valor.Contains("PRODUC"))
            return 10;

        if (valor.Contains("RESPONSABLE") && valor.Contains("TECN"))
            return 20;

        if (valor.Contains("AUX") && valor.Contains("TECN"))
            return 30;

        if (valor.Contains("AUX") && valor.Contains("PRODUC"))
            return 40;

        if (valor.Contains("TECN") && valor.Contains("INYE"))
            return 50;

        if (valor == "SMED" || valor.Contains("SMED"))
            return 60;

        if (valor == "OPERADOR" || valor.Contains("OPERADOR"))
            return 70;

        if (valor.Contains("LIMPIEZA"))
            return 80;

        return 100;
    }

    private static int OrdenNovedadPdfV21(string tipo)
    {
        var valor = (tipo ?? string.Empty)
            .Trim()
            .ToUpperInvariant();

        if (valor.Contains("BAJA")) return 10;
        if (valor.Contains("INGRES")) return 20;
        if (valor.Contains("CAPAC")) return 25;
        if (valor.Contains("INCAP")) return 30;
        if (valor.Contains("VACAC")) return 40;

        return 90;
    }

    private static void PdfMetaHeaderCellV26(
        TableDescriptor table,
        string texto)
    {
        table.Cell()
            .Border(0.5f)
            .BorderColor("#64748B")
            .Background("#13A4D8")
            .PaddingVertical(2.5f)
            .PaddingHorizontal(4)
            .AlignCenter()
            .AlignMiddle()
            .Text(texto ?? string.Empty)
            .Bold()
            .FontSize(6.0f)
            .FontColor(Colors.White);
    }

    private static void PdfMetaValueCellV26(
        TableDescriptor table,
        string texto)
    {
        table.Cell()
            .Border(0.45f)
            .BorderColor("#CBD5E1")
            .PaddingVertical(2.5f)
            .PaddingHorizontal(4)
            .AlignMiddle()
            .Text(texto ?? string.Empty)
            .FontSize(5.8f)
            .FontColor("#334155");
    }

    private static void PdfHeaderCellV26(
        TableDescriptor table,
        string texto)
    {
        table.Cell()
            .Border(0.5f)
            .BorderColor("#64748B")
            .Background("#17365F")
            .PaddingVertical(2.8f)
            .PaddingHorizontal(3)
            .AlignCenter()
            .AlignMiddle()
            .Text(texto ?? string.Empty)
            .Bold()
            .FontSize(5.8f)
            .FontColor(Colors.White);
    }

    private static void PdfBodyCellV26(
        TableDescriptor table,
        string texto,
        bool etiqueta)
    {
        var cell = table.Cell()
            .Border(0.4f)
            .BorderColor("#CBD5E1")
            .PaddingVertical(2.5f)
            .PaddingHorizontal(3)
            .AlignMiddle();

        if (etiqueta)
        {
            cell.Background("#F7F9FC")
                .Text(texto ?? string.Empty)
                .Bold()
                .FontSize(5.6f)
                .FontColor("#233B59");
        }
        else
        {
            cell.Text(texto ?? string.Empty)
                .FontSize(5.5f)
                .FontColor("#2D3E55");
        }
    }

}