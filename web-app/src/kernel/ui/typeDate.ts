// Ayuda de las pruebas: escribir una fecha ISO en un campo de fecha (DateField) como la escribiría una persona, solo con números y en el
// orden de la compañía (el campo pone solos los separadores). Con Puerto Rico (MDY): '2026-10-21' → se teclea 10212026 y se ve «10/21/2026».
import type { UserEvent } from '@testing-library/user-event'
import { dateToText, onlyDigits } from '../format/dateInput'

export async function typeDate(user: UserEvent, element: HTMLElement, iso: string): Promise<void> {
  await user.clear(element)
  await user.type(element, onlyDigits(dateToText(iso)))
}

/** Cómo se ve un ISO en el campo con el formato de la compañía (por omisión Puerto Rico: «10/21/2026»). */
export const shownDate = (iso: string): string => dateToText(iso)
