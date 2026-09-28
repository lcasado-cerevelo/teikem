// Lote 8A-app — reglas puras de Acomodar (docs/mobile/app-almacen-plan.md §2, pantalla 4). Sin API ni base: solo
// ordena y decide qué mostrar, para poder probarlo sin montar nada.
export interface PutawayTask {
  id: number
  sku: string
  productName: string
  quantity: number | null
  toBinCode: string | null
  assignedToUserId: number | null
}

/** Las tareas de quien tiene el aparato en mano van primero; el resto, más viejas primero (orden de llegada). */
export function sortTasksMineFirst(tasks: PutawayTask[], myUserId: number): PutawayTask[] {
  const mine = tasks.filter((t) => t.assignedToUserId === myUserId)
  const others = tasks.filter((t) => t.assignedToUserId !== myUserId)
  return [...mine, ...others]
}
