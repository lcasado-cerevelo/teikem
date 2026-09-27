// Formularios del kit: react-hook-form + zod. <Form> envuelve el submit: si el API responde con error, pone cada
// error de campo bajo su <Field> (applyProblemDetails) y muestra arriba el título y los errores sin campo.
import { useCallback, useContext, useEffect, useId, useMemo, useState, type InputHTMLAttributes, type ReactNode, type SelectHTMLAttributes, type TextareaHTMLAttributes } from 'react'
import { FormProvider, get, useFormContext, type FieldValues, type UseFormReturn } from 'react-hook-form'
import { applyProblemDetails, type AppliedProblem } from '../api/problem'
import { FieldContext, FieldRegistryContext, useFieldInfo, type FieldRegistry } from './formContext'
import './ui.css'

export interface FormProps<TFieldValues extends FieldValues, TContext, TTransformed> {
  /** Resultado de `useForm({ resolver: zodResolver(schema), defaultValues })`. */
  form: UseFormReturn<TFieldValues, TContext, TTransformed>
  /** Guardar. Si lanza (ApiError), los errores del servidor se muestran solos. */
  onSubmit: (values: TTransformed) => Promise<unknown> | unknown
  /** Aviso adicional tras un error del servidor (p. ej. `toast.error(p.title)`). */
  onError?: (problem: AppliedProblem) => void
  /** id del <form>: permite un botón submit fuera (pie del Modal) con `form={id}`. */
  id?: string
  className?: string
  children: ReactNode
}

interface FormAlert {
  title: string
  messages: string[]
}

export function Form<TFieldValues extends FieldValues, TContext, TTransformed>({
  form,
  onSubmit,
  onError,
  id,
  className,
  children,
}: FormProps<TFieldValues, TContext, TTransformed>) {
  const [alert, setAlert] = useState<FormAlert | null>(null)
  // nombres de los <Field> montados (conjunto mutable estable; no provoca renders)
  const [names] = useState(() => new Set<string>())
  const registry = useMemo<FieldRegistry>(
    () => ({
      add: (name) => names.add(name),
      remove: (name) => names.delete(name),
    }),
    [names],
  )

  const submit = form.handleSubmit(async (values) => {
    setAlert(null)
    try {
      await onSubmit(values)
    } catch (err) {
      const problem = applyProblemDetails(err, form)
      // errores sin <Field> que los muestre: arriba, junto al título
      const entries = Object.entries(problem.errors)
      const orphans = entries.filter(([name]) => !names.has(name))
      if (orphans.length > 0 || entries.length === 0) {
        setAlert({ title: problem.title, messages: orphans.flatMap(([, messages]) => messages) })
      }
      onError?.(problem)
    }
  })

  return (
    <FormProvider {...form}>
      <FieldRegistryContext.Provider value={registry}>
        <form id={id} className={className} noValidate onSubmit={submit}>
          {alert && (
            <div className="form-alert" role="alert">
              {alert.title}
              {alert.messages.length > 0 && (
                <ul>
                  {alert.messages.map((m) => (
                    <li key={m}>{m}</li>
                  ))}
                </ul>
              )}
            </div>
          )}
          {children}
        </form>
      </FieldRegistryContext.Provider>
    </FormProvider>
  )
}

export interface FieldProps {
  /** Nombre del campo en el formulario (camelCase, igual que el DTO del API). */
  name: string
  label: ReactNode
  /** Pinta el asterisco; la validación la hace el esquema zod. */
  required?: boolean
  help?: ReactNode
  className?: string
  /** Un control del kit: TextInput, NumberInput, Select, DateInput, Toggle, TextArea (o ClientPickerInput). */
  children: ReactNode
}

/** Etiqueta + control + ayuda + error (del esquema o del servidor) bajo el campo. */
export function Field({ name, label, required = false, help, className, children }: FieldProps) {
  const id = useId()
  const registry = useContext(FieldRegistryContext)
  const { formState } = useFormContext()
  const error = get(formState.errors, name) as { message?: string } | undefined
  const message = error?.message
  const helpId = help ? `${id}-help` : undefined
  const errId = message ? `${id}-err` : undefined

  useEffect(() => {
    registry?.add(name)
    return () => registry?.remove(name)
  }, [registry, name])

  const info = useMemo(
    () => ({
      name,
      id,
      invalid: Boolean(error),
      required,
      describedBy: [errId, helpId].filter(Boolean).join(' ') || undefined,
    }),
    [name, id, error, required, errId, helpId],
  )

  return (
    <div className={['f', className].filter(Boolean).join(' ')}>
      <label htmlFor={id}>
        {label}
        {required && (
          <span className="req" aria-hidden="true">
            *
          </span>
        )}
      </label>
      <FieldContext.Provider value={info}>{children}</FieldContext.Provider>
      {help && !message && (
        <p id={helpId} className="help">
          {help}
        </p>
      )}
      {message && (
        <p id={errId} className="ferr" role="alert">
          {message}
        </p>
      )}
    </div>
  )
}

function useControlProps(component: string) {
  const info = useFieldInfo(component)
  return {
    info,
    aria: {
      id: info.id,
      'aria-invalid': info.invalid || undefined,
      'aria-required': info.required || undefined,
      'aria-describedby': info.describedBy,
    },
  }
}

type NativeInput = Omit<InputHTMLAttributes<HTMLInputElement>, 'name' | 'id' | 'type' | 'value' | 'defaultValue' | 'onChange'>

export interface TextInputProps extends NativeInput {
  type?: 'text' | 'email' | 'tel' | 'url' | 'password' | 'search'
}

/** Texto. Valor: string. */
export function TextInput({ type = 'text', ...rest }: TextInputProps) {
  const { info, aria } = useControlProps('TextInput')
  const { register } = useFormContext()
  return <input type={type} {...rest} {...aria} {...register(info.name)} />
}

/** Número. Valor: number | null (vacío = null). Esquema: `z.number().nullable()` (o `.min(...)`). */
export function NumberInput({ step = 'any', ...rest }: NativeInput) {
  const { info, aria } = useControlProps('NumberInput')
  const { register } = useFormContext()
  const setValueAs = useCallback((v: unknown) => (v === '' || v === null || v === undefined ? null : Number(v)), [])
  return <input type="number" inputMode="decimal" step={step} {...rest} {...aria} {...register(info.name, { setValueAs })} />
}

/** Fecha 'YYYY-MM-DD' ('' = vacía). */
export function DateInput(props: NativeInput) {
  const { info, aria } = useControlProps('DateInput')
  const { register } = useFormContext()
  return <input type="date" {...props} {...aria} {...register(info.name)} />
}

export interface SelectOption {
  value: string
  label: string
}

export interface SelectProps extends Omit<SelectHTMLAttributes<HTMLSelectElement>, 'name' | 'id' | 'value' | 'defaultValue' | 'onChange'> {
  options: readonly SelectOption[]
  /** Opción vacía ('') al principio, p. ej. "Seleccione…". */
  placeholder?: string
}

/** Lista de opciones (valor string; '' = sin elegir). Para catálogos: opciones de `useLookups`. */
export function Select({ options, placeholder, ...rest }: SelectProps) {
  const { info, aria } = useControlProps('Select')
  const { register } = useFormContext()
  return (
    <select {...rest} {...aria} {...register(info.name)}>
      {placeholder !== undefined && <option value="">{placeholder}</option>}
      {options.map((o) => (
        <option key={o.value} value={o.value}>
          {o.label}
        </option>
      ))}
    </select>
  )
}

export interface ToggleProps extends Omit<NativeInput, 'checked'> {
  /** Texto junto al interruptor (p. ej. "Activo"). */
  text?: ReactNode
}

/** Interruptor (`.sw`). Valor: boolean. */
export function Toggle({ text, ...rest }: ToggleProps) {
  const { info, aria } = useControlProps('Toggle')
  const { register } = useFormContext()
  return (
    <label className="sw">
      <input type="checkbox" role="switch" {...rest} {...aria} {...register(info.name)} />
      <span className="tk" aria-hidden="true" />
      {text != null && <span>{text}</span>}
    </label>
  )
}

/** Texto largo. Valor: string. */
export function TextArea({ rows = 3, ...rest }: Omit<TextareaHTMLAttributes<HTMLTextAreaElement>, 'name' | 'id' | 'value' | 'defaultValue' | 'onChange'>) {
  const { info, aria } = useControlProps('TextArea')
  const { register } = useFormContext()
  return <textarea rows={rows} {...rest} {...aria} {...register(info.name)} />
}
