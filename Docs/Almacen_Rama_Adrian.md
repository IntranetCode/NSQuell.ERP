# Módulo de Almacén — Rama_Adrian

## Estado analizado

La rama `Rama_Adrian` está alineada con `desarrollo` en el commit revisado. Los cambios de este paquete se limitan a Almacén y no modifican controladores, vistas ni tablas de Planeación.

## Alcance implementado hasta el 13/07/2026

- Inventario MP con catálogo `ERP_Materiales`.
- Inventario PT reutilizando `ERP_Partes` y `ERP_Clientes`.
- Catálogo compartido `ERP_Ubicaciones`.
- Entradas, salidas, retornos, consumo, scrap y ajustes MP.
- Entradas por caja y movimientos PT: salida, embarque, retención, liberación, retorno, scrap y ajustes. Los movimientos nuevos de PT requieren `CajaID` para mantener trazabilidad.
- Vistas de existencias MP, PT y PT por caja.
- Validación de stock antes de salidas.
- Listados, indicadores y tablas compactas.
- Semáforo aplicado directamente sobre `Stock disponible`.
- Estado `SIN_CONFIGURAR` hasta que Almacén confirme los niveles del registro, incluso si el valor correcto es 0/0.
- Pantallas masivas para capturar `StockMinimo` y `StockAviso` en MP y PT.
- Registro de `NumeroOF` sin modificar Planeación.
- Referencia de operación única para impedir descuentos duplicados.
- Endpoints de consulta y descuento preparados para Planeación.
- Script de diagnóstico de solo lectura.

## Diferencia entre existencia y niveles de stock

- La **existencia real** no se escribe directamente en el catálogo. Se calcula a partir de movimientos.
- MP se carga desde `/AlmacenMP/Movimiento?tipo=Entrada`.
- PT se carga desde `/AlmacenPT/Entrada`, registrando caja, etiqueta y cantidad. Una entrada con calidad distinta de `Liberado` queda bloqueada automáticamente.
- `StockMinimo` y `StockAviso` solo determinan el color y las alertas.
- No se asignaron cantidades mínimas inventadas: deben capturarse con el listado validado por Almacén/Compras/Producción.

## Pantallas nuevas

- `/AlmacenMP/NivelesStock`
- `/AlmacenPT/NivelesStock`

Ambas permiten filtrar pendientes y guardar hasta 100 niveles visibles en una sola operación; se puede buscar para trabajar por bloques.

## Integración disponible para Planeación

### Consulta

- `GET /AlmacenIntegracion/StockMP?codigo=...&cantidad=...`
- `GET /AlmacenIntegracion/StockPT?numeroParte=...&cantidad=...`
- `GET /AlmacenIntegracion/CajasPT?numeroParte=...` para seleccionar una caja física con existencia

La respuesta incluye disponible, requerido, suficiencia, mínimo, aviso, estado configurado y semáforo.

### Descuento idempotente

- `POST /AlmacenIntegracion/DescontarMP`
- `POST /AlmacenIntegracion/DescontarPT`

Los POST requieren antiforgery y una `ReferenciaOperacion` única. Si Planeación repite la misma solicitud por error, Almacén no descuenta dos veces. En PT, `CajaID` es obligatorio para conservar trazabilidad física y evitar diferencias entre el saldo por parte y el saldo por caja.

Ejemplos de referencia:

- `PLN-OF-000123-MP-02-10-003-12`
- `PLN-OF-000123-PT-ABC-001`

## Orden de ejecución SQL

1. Respaldar `ERP_QUELL`.
2. Si Almacén aún no existe, ejecutar `01_Estructura_Almacen_MP_PT.sql`.
3. Si corresponde, ejecutar `02_Importar_Catalogo_MP_Legacy.sql`.
4. Ejecutar `04_Actualizar_Stock_e_Integracion_Almacen.sql`.
5. Ejecutar `90_Diagnostico_Almacen_Hasta_13_Julio.sql`.
6. No ejecutar `03_Importar_Movimientos_MP_Legacy.sql` como stock real sin validar: contiene datos `script-demo` y `script-stock-inicial`.

## Pruebas mínimas

1. Configurar mínimo y aviso de un material MP.
2. Registrar una entrada MP y confirmar que cambia `Stock disponible`.
3. Intentar una salida mayor al disponible y confirmar el bloqueo.
4. Configurar mínimo y aviso de un número de parte PT.
5. Registrar una entrada PT por caja; si entra con estado distinto de Liberado debe quedar retenida automáticamente.
6. Retener y liberar piezas de calidad y confirmar el saldo por caja.
7. Consultar los endpoints `StockMP` y `StockPT`.
8. Ejecutar dos veces un descuento con la misma `ReferenciaOperacion`; el segundo no debe descontar.
9. Ejecutar el diagnóstico y confirmar que no existen referencias duplicadas.

## Seguridad

- Todos los POST usan antiforgery.
- Las salidas se registran bajo transacción serializable y validan saldo.
- Los descuentos externos usan referencia única.
- El SQL no modifica menús, permisos, usuarios, compras ni Planeación.
- `ERP_Partes` conserva el catálogo existente y únicamente utiliza `StockMinimo`, `StockAviso` y `StockConfigurado`.

## Rollback

- Código: `git apply -R <archivo.patch>` antes de hacer commit.
- El script `04` solo agrega columnas/índices y reemplaza vistas; no elimina movimientos ni catálogos.
- Para revertir únicamente la integración, usar `98_Rollback_Stock_e_Integracion_Almacen.sql` después de revisar y habilitar `@Confirmar`.
- El rollback total del módulo sigue siendo `99_Rollback_Almacen_MP_PT.sql` y debe revisarse antes de habilitarlo.

## Navegación del módulo

- Listado central de almacenes: `/Almacen/Index`.
- Inventario MP: `/AlmacenMP/Index`.
- Inventario PT: `/AlmacenPT/Index`.
- Listado de almacenes, racks y ubicaciones: `/AlmacenUbicaciones/Index`.
- Los formularios y submenús incluyen una acción **Regresar** hacia su listado correspondiente.

## Corrección de metadatos de stock

El script `04_Actualizar_Stock_e_Integracion_Almacen.sql` utiliza lotes dinámicos para crear y consultar `StockConfigurado` y `ReferenciaOperacion`. Esto evita los errores de compilación de SQL Server cuando una columna se agrega y se consulta dentro del mismo lote.

## Cierre funcional al 14 de julio de 2026

Se agregaron funciones exclusivas de Almacén para completar el alcance previo a Planeación:

- Historial completo MP: `/AlmacenMP/Historial`.
- Historial completo PT: `/AlmacenPT/Historial`.
- Filtros por fechas, movimiento, OF, responsable, lote y etiqueta.
- Exportación CSV compatible con Excel, limitada a 10,000 registros por descarga.
- Referencia única automática para todos los movimientos manuales nuevos.
- Script controlado de carga inicial: `05_Carga_Inicial_Controlada_Almacen.sql`.
- Normalización controlada de referencias históricas: `06_Normalizar_Auditoria_Almacen.sql`.
- Script de validación de cierre: `91_Validacion_Cierre_Almacen_14_Julio.sql`.
- Accesos al historial desde los inventarios y el listado central de Almacén.

### Orden recomendado para el cierre

1. Ejecutar `04_Actualizar_Stock_e_Integracion_Almacen.sql` corregido.
2. Configurar niveles MP y PT desde las pantallas masivas.
3. Capturar conteos reales en `05_Carga_Inicial_Controlada_Almacen.sql`.
4. Ejecutar primero con `@Confirmar = 0`.
5. Revisar la previsualización y después ejecutar con `@Confirmar = 1`.
6. Ejecutar `06_Normalizar_Auditoria_Almacen.sql` primero en previsualización.
7. Probar todos los movimientos manuales.
8. Ejecutar `91_Validacion_Cierre_Almacen_14_Julio.sql`.
9. Revisar los historiales y sus exportaciones.

### Fuera de alcance temporal

La lectura de requerimientos de la OF, el apartado y el descuento desde Planeación quedan pendientes hasta recibir e integrar el pull correspondiente. No se deben modificar tablas, controladores ni estados de Planeación desde estos scripts.

## Trazabilidad por material y número de parte

- En inventario, catálogo y niveles de stock, el código MP y el número de parte PT son enlaces.
- Al seleccionarlos se abre el historial completo con el material o la parte ya filtrados.
- Los filtros de material, parte, OF, responsable, lote y etiqueta permiten escribir libremente y muestran opciones existentes mediante listas de selección.
- El historial general sigue disponible sin filtros.

## Stock PT para pruebas

1. Ejecutar `Scripts/SQL/Almacen/07_Cargar_Stock_PT_Pruebas.sql` con `@Confirmar = 0` para revisar la previsualización.
2. Cambiar `@Confirmar = 1` para crear cinco cajas liberadas con 100 piezas cada una.
3. Probar entradas, salidas, retenciones, liberaciones, ajustes e historial.
4. Para retirar únicamente estas cajas y sus movimientos, ejecutar `97_Limpiar_Stock_PT_Pruebas.sql` con `@Confirmar = 1`.
