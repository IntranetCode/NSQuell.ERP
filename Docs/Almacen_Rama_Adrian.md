# Módulo de Almacén — Rama_Adrian

## Alcance

- Inventario MP con catálogo `ERP_Materiales`.
- Inventario PT reutilizando `ERP_Partes` y `ERP_Clientes`.
- Catálogo compartido `ERP_Ubicaciones`.
- Entradas, salidas, retornos, consumo, scrap y ajustes MP.
- Entradas por caja y movimientos PT: salida, embarque, retención, liberación, retorno, scrap y ajustes.
- Semáforo configurable por material o número de parte.
- Validación de stock antes de salidas.
- Registro opcional de `NumeroOF` sin modificar Planeación.
- Endpoints de consulta para integración futura:
  - `/AlmacenIntegracion/StockMP?codigo=...&cantidad=...`
  - `/AlmacenIntegracion/StockPT?numeroParte=...&cantidad=...`

## Orden de ejecución

1. Respaldar `ERP_QUELL`.
2. Ejecutar `Scripts/SQL/Almacen/01_Estructura_Almacen_MP_PT.sql`.
3. Ejecutar `02_Importar_Catalogo_MP_Legacy.sql` para cargar ubicaciones y el catálogo MP anterior.
4. Revisar `03_Importar_Movimientos_MP_Legacy.sql`. El respaldo contiene filas `script-stock-inicial` y `script-demo`; solo cambiar `@ConfirmarMovimientos` a `1` después de validarlas.
5. Compilar y probar:
   - `/AlmacenMP/Index`
   - `/AlmacenPT/Index`
   - `/AlmacenUbicaciones/Index`

## Seguridad

- Todos los POST usan antiforgery.
- Las salidas se registran bajo transacción serializable y validan saldo.
- El SQL no modifica menús, permisos, usuarios, compras ni Planeación.
- `ERP_Partes` únicamente recibe dos columnas aditivas: `StockMinimo` y `StockAviso`.

## Rollback

- Código: `git apply -R <archivo.patch>` antes de hacer commit.
- Base de datos: revisar y ejecutar `99_Rollback_Almacen_MP_PT.sql` cambiando `@Confirmar` a `1`.
