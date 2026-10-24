// Centro de Puerto Rico — fallback when an address can't be geocoded.
export const defaultLocation = { lat: 18.220833, lng: -66.590149 };

// Username (email) validation.
export const EmailRegex =
  /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

// (Se retiró ORDERS_PAGE_SIZE = 20.) OrdersList cortaba el listado a las
// primeras 20 órdenes "matching original behavior", lo que además de esconderle
// entregas a un chofer con más de 20, hacía que el buscador solo encontrara
// dentro de ese primer tramo. Ahora carga todas: el detalle de cada orden ya
// viene en la caché persistida desde la sincronización, así que no cuesta red,
// y la FlatList virtualiza el render.
