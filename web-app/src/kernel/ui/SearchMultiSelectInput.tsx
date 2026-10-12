// Variante de `SearchMultiSelect` para un <Field> del kit (Lote F-E2a): el valor del formulario es un arreglo de strings.
import { useController, useFormContext } from 'react-hook-form'
import { useFieldInfo } from './formContext'
import { SearchMultiSelect, type SearchMultiSelectProps } from './SearchSelect'

export type SearchMultiSelectInputProps = Omit<SearchMultiSelectProps, 'value' | 'onChange' | 'id' | 'invalid' | 'describedBy' | 'onBlur' | 'buttonRef'>

/** Selección múltiple con buscador dentro de un `<Field name>`: `<Field name="zoneIds" label="Zonas"><SearchMultiSelectInput options={zones} /></Field>`. */
export function SearchMultiSelectInput(props: SearchMultiSelectInputProps) {
  const info = useFieldInfo('SearchMultiSelectInput')
  const { control } = useFormContext()
  const { field } = useController({ name: info.name, control })
  return (
    <SearchMultiSelect
      {...props}
      id={info.id}
      buttonRef={field.ref}
      value={(field.value as string[] | undefined) ?? []}
      onChange={(next) => field.onChange(next)}
      onBlur={field.onBlur}
      invalid={info.invalid}
      describedBy={info.describedBy}
    />
  )
}
