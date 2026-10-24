// Mensajes de validación del consignatario (esquema `consigneeSchema`), compartidos por el modal y el formulario en línea.
import type { useT } from '../../kernel/i18n/useT'
import type { ConsigneeMessages } from './consigneeRules'

/** `t` de `useT()`: arma los mensajes `consignees.errors.*` en el idioma actual. */
export function consigneeMessagesOf(t: ReturnType<typeof useT>): ConsigneeMessages {
  return {
    nameRequired: t('consignees.errors.nameRequired'),
    line1Required: t('consignees.errors.line1Required'),
    cityRequired: t('consignees.errors.cityRequired'),
    countryRequired: t('consignees.errors.countryRequired'),
    max: (max) => t('consignees.errors.max', { max }),
    minutesInteger: t('consignees.errors.minutesInteger'),
    minutesNegative: t('consignees.errors.minutesNegative'),
    windowPair: t('consignees.errors.windowPair'),
    windowOrder: t('consignees.errors.windowOrder'),
    phoneInvalid: t('consignees.errors.phoneInvalid'),
  }
}
