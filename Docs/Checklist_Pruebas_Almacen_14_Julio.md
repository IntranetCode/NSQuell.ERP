# Checklist de pruebas — Almacén MP/PT

Fecha objetivo: 14/07/2026

Este checklist cubre únicamente Almacén. La conexión con Planeación y OF queda pendiente hasta integrar su pull.

## Preparación

- [ ] Ejecutar `04_Actualizar_Stock_e_Integracion_Almacen.sql` corregido.
- [ ] Ejecutar `90_Diagnostico_Almacen_Hasta_13_Julio.sql` sin errores de estructura.
- [ ] Configurar al menos un material MP y un número de parte PT con stock mínimo y de aviso.
- [ ] Confirmar que existen ubicaciones activas MP y PT.
- [ ] Ejecutar `dotnet build` sin errores.

## Materia prima

- [ ] Registrar una entrada MP con lote y ubicación.
- [ ] Verificar que aumenten `Entradas` y `Stock disponible`.
- [ ] Registrar una salida menor al disponible.
- [ ] Confirmar que una salida mayor al disponible sea rechazada.
- [ ] Registrar consumo, retorno y scrap.
- [ ] Registrar ajuste positivo.
- [ ] Registrar ajuste negativo.
- [ ] Confirmar que un material con `RequiereLote = 1` no acepte `S/L`.
- [ ] Revisar cambio de semáforo según mínimo y aviso.
- [ ] Consultar el movimiento en `/AlmacenMP/Historial`.
- [ ] Filtrar historial por fecha, tipo, lote y responsable.
- [ ] Exportar CSV y abrirlo en Excel.

## Producto terminado

- [ ] Registrar una caja PT con estado `Liberado`.
- [ ] Confirmar que la cantidad quede disponible.
- [ ] Registrar una caja con estado `Retenido`, `GP12` o `Cuarentena`.
- [ ] Confirmar que la entrada retenida no quede disponible.
- [ ] Registrar una salida por caja.
- [ ] Confirmar rechazo cuando la salida sea mayor al disponible de la caja.
- [ ] Registrar retención parcial y liberación parcial.
- [ ] Confirmar que no se pueda liberar más de lo retenido.
- [ ] Registrar retorno, scrap, ajuste positivo y ajuste negativo.
- [ ] Confirmar consistencia entre existencia por caja y por número de parte.
- [ ] Consultar el movimiento en `/AlmacenPT/Historial`.
- [ ] Filtrar historial por fecha, tipo, etiqueta, lote y responsable.
- [ ] Exportar CSV y abrirlo en Excel.

## Navegación e interfaz

- [ ] Verificar `/Almacen/`.
- [ ] Verificar el botón `Regresar` hacia `/Menu/Grupo/1`.
- [ ] Revisar inventarios, catálogos, movimientos, stock e historiales en 1366×768.
- [ ] Revisar las mismas pantallas con ventana menor a 820 px.
- [ ] Confirmar que tablas grandes mantengan desplazamiento horizontal controlado.
- [ ] Confirmar mensajes de éxito, advertencia y validación.

## Cierre de base de datos

- [ ] Ejecutar `06_Normalizar_Auditoria_Almacen.sql` y confirmar el backfill de referencias.
- [ ] Ejecutar `91_Validacion_Cierre_Almacen_14_Julio.sql`.
- [ ] Confirmar cero saldos negativos.
- [ ] Confirmar cero referencias duplicadas.
- [ ] Confirmar cero cajas relacionadas con un número de parte incorrecto.
- [ ] Revisar materiales y partes todavía sin niveles configurados.
- [ ] Guardar evidencia del resultado general.
