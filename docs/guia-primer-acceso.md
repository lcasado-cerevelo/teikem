# Teikem — Guía de primer acceso

Para quien va a entrar por primera vez a Teikem: cómo entrar, crear un usuario de almacén, crear y registrar un aparato (teléfono)
y dar el PIN. Está escrita en el orden en que se hace.

**Direcciones**
- Web: **https://teikem.advancelogisticspr.com**
- App del almacén: el APK de *Teikem Almacén* (se instala en el teléfono; la dirección del servidor ya viene escrita).

---

## 1. Qué usuarios existen y qué pueden hacer

| Correo | Nombre | Qué es | Compañías |
|---|---|---|---|
| `teikem+admin@cerevelo.com` | Administrador Advance | **Administrador de compañía** (rol *Admin de compañía*): hace todo dentro de cada compañía | Advance Logistics, Advance Depot y Advance Solutions |
| `teikem+support@cerevelo.com` | Soporte Teikem | **Administrador de la plataforma**: ve y administra **todas** las compañías (soporte) | Todas |
| `teikem+dispatch@cerevelo.com` | Carlos Rivera | **Despachador** (usuario de prueba): órdenes, rutas y despacho | Advance Logistics |

- **Compañías:** *Advance Depot* y *Advance Solutions* son las compañías reales; *Advance Logistics* es la compañía de demostración.
- **Contraseña inicial** de esos tres usuarios: la que quedó en la configuración de la base, **`Teikem_Admin_2026!`** (si la base se creó con
  otra, es la de `Seed:Demo:AdminPassword`). **Es temporal**: el sistema obliga a cambiarla en el primer ingreso (paso 2).
- Si la base se creó antes del 1 de octubre de 2026, es posible que esos usuarios **no** pidan el primer ingreso: en ese caso entran directo;
  cambie la contraseña desde **Mi cuenta → Contraseña** y active el MFA desde **Mi cuenta → Verificación en dos pasos**.
- Los usuarios que usted cree después tendrán la contraseña que usted les dé (o una temporal que el sistema genera) y también la
  cambian en su primer ingreso.

### Roles de fábrica (cada compañía tiene su propia copia)

| Rol | Para quién |
|---|---|
| Admin de compañía | Dueño o gerente: todo en su compañía |
| Despachador | Órdenes, rutas, despacho |
| Facturación | Facturación y cobros |
| **Operador de almacén** | **El guarda de almacén** (ver el paso 3) |
| Chofer | Entregas |
| Solo lectura | Consulta sin cambiar nada |

---

## 2. Entrar por primera vez (los tres pasos obligatorios)

Todo usuario, la primera vez, completa **tres pasos en orden**. Hasta terminarlos no puede usar nada.

1. Abra **https://teikem.advancelogisticspr.com** y entre con su **correo** y la **contraseña inicial**.
2. **Verificar el correo.** Toque *Enviar código*: le llega un **código de 6 dígitos** a su correo (se muestra enmascarado, por
   ejemplo `t***@cerevelo.com`). Escríbalo y toque *Verificar*. Si no llega, revise correo no deseado y toque *Enviar otro código*.
3. **Poner su propia contraseña.** Mínimo **12 caracteres**, escrita dos veces. No puede ser la contraseña que le dieron.
4. **Activar la verificación en dos pasos (MFA).** Instale una app de autenticación en su teléfono (Google Authenticator, Microsoft
   Authenticator, 1Password…), **escanee el código QR** (o escriba la clave), y escriba el código de 6 dígitos que muestra la app.
5. **Guarde los códigos de recuperación.** Salen **una sola vez**: toque *Descargar* o *Copiar*, guárdelos **fuera del teléfono** (en un
   lugar seguro) y marque *Ya los guardé*. Sirven si pierde el teléfono (cada uno sirve una vez).

Listo: ya está dentro. **A partir de ahí**, cada vez que entre le pedirá el código de 6 dígitos de su app (o un código de recuperación).

> Si una compañía le asigna más de una, arriba a la derecha (junto al reloj) tiene un **selector de compañía** para cambiar entre ellas
> sin volver a entrar. El administrador de la plataforma ve todas.

---

## 3. Crear el usuario del almacén (recomendado)

**Sugerencia:** crear **un usuario para el almacén** con el rol **Operador de almacén** (por ejemplo, *Guarda Almacén*). Ese rol es el
adecuado para el teléfono: recibe, acomoda, despacha, cuenta e inventaría, **sin** acceso a rutas ni a compras.

Con un administrador (`teikem+admin@…`), **en la compañía donde va a trabajar el guarda**:

1. Menú **Sistema → Roles y usuarios** → botón **Nuevo usuario**.
2. Escriba el **correo** (obligatorio) y el **nombre**.
3. En **Roles** elija **Operador de almacén** (el nombre del rol en pantalla es *WarehouseOperator*).
4. **Contraseña:** escriba una, o déjela vacía y el sistema genera una **temporal que se muestra una sola vez** (cópiela antes de
   cerrar y entréguela por un canal seguro).
5. Si ese usuario va a trabajar en **más de una compañía**, marque las otras en **También agregar a estas compañías** (recibe el mismo
   rol, por nombre, en cada una; solo salen las compañías donde usted administra usuarios).
6. *Guardar*.

**Importante:** el nuevo usuario tiene que **entrar una vez a la web** y completar los **tres pasos del primer ingreso** (sección 2) **antes**
de poder usar su PIN en el teléfono. Si no, el teléfono responde *"Complete primero su primer ingreso en la web…"*.

---

## 4. Crear el aparato (el teléfono) en Teikem

El aparato se crea **en la compañía** donde va a trabajar (el teléfono queda ligado a esa compañía, no a un usuario).

1. Con el administrador, en esa compañía: **Sistema → Aparatos → Nuevo aparato**.
2. Escriba el **Nombre** (obligatorio, por ejemplo *Zebra 01*) y, recomendado, el **Almacén por defecto**.
3. Al guardar, el sistema muestra el **código de registro** (8 caracteres). **Se muestra una sola vez**, es de **un solo uso** y
   **vence en 24 horas**. Cópielo o anótelo.
   - Si vence o se pierde: en la fila del aparato, **Nuevo código de registro** (el anterior deja de servir).

---

## 5. Dar el PIN al usuario

El PIN es la clave corta (**4 a 6 dígitos**) con la que el guarda entra a la app sin contraseña. Hay dos formas:

- **Lo fija el administrador:** *Sistema → Roles y usuarios*, en la fila del usuario, **Asignar PIN** (pide confirmar su identidad otra vez). Entregue
  el PIN al guarda; que lo cambie por uno suyo.
- **Lo fija el propio usuario** (mejor): en la web, **Mi cuenta → PIN de la app → Fijar PIN** (pide su contraseña).

Cinco intentos fallidos bloquean el PIN 15 minutos.

---

## 6. Registrar el aparato en el teléfono (con el código)

1. Instale el **APK de Teikem Almacén** en el teléfono (copie el archivo y ábralo; permita *instalar apps desconocidas* si lo pide).
2. Abra la app. La pantalla **Registrar este aparato** ya trae la **dirección del servidor**.
3. Escriba el **código de registro** (paso 4) y toque **Registrar**. El teléfono queda ligado a la compañía donde se creó el aparato.
4. Aparece **"¿Quién eres?"** con los usuarios de esa compañía. Toque el nombre del guarda, escriba su **PIN** y entre.

### ¿Quién aparece en "¿Quién eres?"?
Solo los usuarios que cumplen **las cinco**: interno y activo · miembro de **la compañía del aparato** · con **PIN** · con el permiso de ver
inventario (el *Operador de almacén* lo tiene) · y que **ya completó su primer ingreso** en la web.

### Un teléfono en varias compañías
En **"¿Quién eres?"** toque **Registrar otra compañía** y escriba un código de un aparato creado en la otra compañía. Desde entonces, al entrar
la app pregunta **"¿En qué compañía vas a trabajar?"**. Cada compañía guarda sus propios datos en el teléfono. Un teléfono se registra **una sola vez
por compañía** (si repite una, avisa y **no gasta el código**).

---

## 7. Resumen en orden

| # | Quién | Dónde | Qué |
|---|---|---|---|
| 1 | Administrador | Web | Primer ingreso: correo → contraseña propia → MFA (con QR) → guardar códigos |
| 2 | Administrador | Sistema → Roles y usuarios | Crear el usuario del guarda con el rol **Operador de almacén** |
| 3 | El guarda | Web | Su primer ingreso (correo, contraseña, MFA) |
| 4 | Administrador | Sistema → Aparatos | Crear el aparato → copiar el **código de registro** (24 h) |
| 5 | Administrador o el guarda | Roles y usuarios (*Asignar PIN*) o Mi cuenta | Dar el **PIN** (4–6 dígitos) |
| 6 | El guarda | Teléfono | Registrar con el código → elegir su nombre → PIN → trabajar |

---

## 8. Problemas frecuentes

| Qué pasa | Qué hacer |
|---|---|
| *"El código de registro no es válido o venció."* | Dura 24 h y sirve una vez. En Aparatos: **Nuevo código de registro**. |
| El guarda no sale en "¿Quién eres?" | Revise las cinco condiciones del paso 6 (sobre todo: **PIN** y **primer ingreso hecho**). |
| *"Complete primero su primer ingreso en la web…"* | Ese usuario debe entrar una vez a la web y completar los tres pasos. |
| *"PIN bloqueado por 15 minutos."* | Espere, o fije un PIN nuevo (administrador o Mi cuenta). |
| *"Este teléfono ya está registrado en … como …"* | Ya está registrado en esa compañía; use ese registro, o pida un código de otra compañía. |
| El código del correo no llega | Revise correo no deseado y toque *Enviar otro código*. Si persiste, avise a soporte (el envío usa Brevo). |
| Perdí el teléfono de la app de autenticación | Entre con un **código de recuperación**; si no los tiene, un administrador le **reinicia el MFA** (Sistema → Roles y usuarios) y lo configura de nuevo. |
| En la app sale "No se pudo sincronizar" | Toque **Sincronizar ahora**. Si sigue, avise a soporte (puede ser falta de señal o del servidor). |

---

## 9. Recomendaciones de seguridad

- **Cambie las contraseñas iniciales de inmediato** (el primer ingreso lo obliga) y **guarde los códigos de recuperación** fuera del teléfono.
- Cada persona con **su propio usuario**: no comparta `teikem+admin@…` ni `teikem+support@…` para el trabajo diario. Use el usuario de
  soporte solo para administrar; para el almacén, el rol **Operador de almacén**.
- Si un teléfono se pierde o se roba: **Sistema → Aparatos → Desactivar** (se cierran sus sesiones).
- La compañía de demostración (**Advance Logistics**) y el usuario `teikem+dispatch@…` son de prueba: desactívelos cuando no se necesiten.
