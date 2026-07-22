# Producción — Primera entrega funcional

## Objetivo

Entregar una pantalla utilizable mientras se completa el análisis de la base de datos operativa.

## Ruta

```text
/Produccion/Index
```

El submenú `Ver Producción` ya existe en la base y apunta a esa ruta.

## Fuente de datos

```text
dbo.Planeacion_ProgramaProduccion
```

La tabla ya contiene registros y datos de:

- OF relacionada;
- cliente;
- parte y referencia SAP;
- máquina;
- molde;
- cantidades;
- programación;
- inicio y fin real;
- materiales;
- embalaje;
- estado.

## Alcance de esta versión

- Consulta real de programas activos.
- KPIs de programado, producido y pendiente.
- Búsqueda.
- Filtro por estatus.
- Indicador de avance.
- Detección visible de registros incompletos.
- Navegación a la OF de Planeación.
- Diseño responsive.
- CSS separado en `wwwroot/css/Produccion`.

## Restricciones actuales

Esta versión no modifica información.

Todavía no habilita:

- preparar;
- iniciar;
- pausar;
- reanudar;
- capturar producción;
- registrar scrap;
- registrar paros;
- validar;
- cerrar.

Estas operaciones requieren confirmar el modelo de datos para evitar duplicar `Planeacion_ProgramaProduccion`.

## Actividades del primer día

| Actividad | Resultado |
|---|---|
| Análisis de BDD | Avance suficiente para identificar la fuente inicial; diagnóstico profundo pendiente |
| Flujo operativo | Definido |
| Levantamiento funcional | Definido |
| Revisión del proyecto | Definida la reutilización de OF, partes, máquinas, moldes, usuarios, menú y layout general |

## Archivos creados

```text
Controllers/ProduccionController.cs
Models/ViewModels/Produccion/ProduccionVm.cs
Views/Produccion/Index.cshtml
wwwroot/css/Produccion/produccion.css
Docs/Produccion_Dia1_Primera_Entrega.md
```

No se crea un layout departamental.
No se modifica `ServicioAcceso`.
No se ejecuta SQL.
