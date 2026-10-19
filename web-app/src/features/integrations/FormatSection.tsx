// Sección «Formato del archivo» del formulario de integraciones (Lote 137, P11): CSV (como siempre), hoja de Excel (.xlsx), JSON o XML con mapeo por ruta. Automático decide por la extensión. En JSON y XML
// se declara dónde están las órdenes, las líneas (opcional) y el mapeo ruta → columna, en el orden de la plantilla de importación; «Probar con un archivo» muestra cómo lo leería la plantilla, sin guardar nada.
import { useRef, useState } from 'react'
import { useWatch, type UseFormReturn } from 'react-hook-form'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useT } from '../../kernel/i18n'
import { Field, Select, TextInput } from '../../kernel/ui'
import { FORMAT_KINDS, MAX_COLUMNS, buildFormatConfig, isStructured } from './formatRules'
import type { IntegrationValues } from './integrationRules'
import { usePreviewFormat, type FormatPreviewDto } from './integrationsApi'

export function FormatSection({ form }: { form: UseFormReturn<IntegrationValues> }) {
  const t = useT()
  const format = useWatch({ control: form.control, name: 'format' })
  const preview = usePreviewFormat()
  const fileRef = useRef<HTMLInputElement>(null)
  const [result, setResult] = useState<FormatPreviewDto | null>(null)
  const [error, setError] = useState<string | null>(null)
  const kind = format?.kind ?? 'AUTO'
  const columnsError = (form.formState.errors.format as { columns?: { message?: string } } | undefined)?.columns?.message
  const columns = format?.columns ?? []
  const set = (columnsNext: { header: string; path: string }[]) => form.setValue('format.columns', columnsNext, { shouldDirty: true, shouldValidate: form.formState.isSubmitted })

  const run = async (file: File) => {
    setError(null)
    setResult(null)
    try {
      setResult(await preview.mutateAsync({ file, config: buildFormatConfig(form.getValues('format')) }))
    } catch (e) {
      setError(applyProblemDetails(e).title ?? '')
    }
  }

  return (
    <fieldset className="int-scopes">
      <legend>{t('integrations.format.section')}</legend>
      <p className="help">{t('integrations.format.sectionHelp')}</p>
      <div className="r2">
        <Field name="format.kind" label={t('integrations.format.kind')}>
          <Select options={FORMAT_KINDS.map((k) => ({ value: k, label: t(`integrations.format.kinds.${k}`) }))} />
        </Field>
        {kind === 'EXCEL' && (
          <Field name="format.sheet" label={t('integrations.format.sheet')} help={t('integrations.format.sheetHelp')}>
            <TextInput maxLength={31} />
          </Field>
        )}
      </div>
      {isStructured(kind) && (
        <>
          <p className="help">{t(`integrations.format.pathHelp.${kind}`)}</p>
          <div className="r2">
            <Field name="format.recordsPath" label={t('integrations.format.recordsPath')} required help={t('integrations.format.recordsPathHelp')}>
              <TextInput placeholder={kind === 'XML' ? '$.Pedidos.Orden[*]' : '$.orders[*]'} autoComplete="off" />
            </Field>
            <Field name="format.linesPath" label={t('integrations.format.linesPath')} help={t('integrations.format.linesPathHelp')}>
              <TextInput placeholder={kind === 'XML' ? '@.Linea[*]' : '@.lines[*]'} autoComplete="off" />
            </Field>
          </div>
          <div className="int-cols">
            <div className="int-cols-head">
              <b>{t('integrations.format.columns')}</b>
              <span className="help">{t('integrations.format.columnsHelp')}</span>
            </div>
            {columns.map((_, i) => (
              <div className="int-col" key={i}>
                <span className="help int-col-n">{i + 1}</span>
                <Field name={`format.columns.${i}.header`} label={t('integrations.format.header')}>
                  <TextInput maxLength={80} autoComplete="off" />
                </Field>
                <Field name={`format.columns.${i}.path`} label={t('integrations.format.path')}>
                  <TextInput placeholder="^.ref" autoComplete="off" />
                </Field>
                <button type="button" className="btn sm" aria-label={t('integrations.format.removeColumn', { n: i + 1 })} onClick={() => set(columns.filter((__, j) => j !== i))}>
                  {t('integrations.format.remove')}
                </button>
              </div>
            ))}
            <button type="button" className="btn sm" disabled={columns.length >= MAX_COLUMNS} onClick={() => set([...columns, { header: '', path: '' }])}>
              {t('integrations.format.addColumn')}
            </button>
            {typeof columnsError === 'string' && (
              <p className="ferr" role="alert">
                {columnsError}
              </p>
            )}
          </div>
        </>
      )}
      <div className="int-previewbar">
        <input
          ref={fileRef}
          type="file"
          className="int-file"
          accept=".csv,.txt,.xlsx,.json,.xml"
          aria-label={t('integrations.format.sampleFile')}
          onChange={(e) => {
            const f = e.target.files?.[0]
            e.target.value = ''
            if (f) void run(f)
          }}
        />
        <button type="button" className="btn sm" disabled={preview.isPending} onClick={() => fileRef.current?.click()}>
          {t('integrations.format.test')}
        </button>
        <span className="help">{t('integrations.format.testHelp')}</span>
      </div>
      {error && (
        <p className="ferr" role="alert" data-testid="format-error">
          {error}
        </p>
      )}
      {result && (
        <div className="int-grid-wrap" data-testid="format-preview">
          <p className="help">{t('integrations.format.previewSummary', { kind: t(`integrations.format.kinds.${result.kind}`), rows: result.totalRows ?? 0, shown: result.rows?.length ?? 0 })}</p>
          {(result.sheets?.length ?? 0) > 0 && <p className="help">{t('integrations.format.sheets', { sheets: (result.sheets ?? []).join(', ') })}</p>}
          <table className="lst densetbl int-grid">
            <thead>
              <tr>
                <th>#</th>
                {(result.header ?? []).map((h, i) => (
                  <th key={i}>{h}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {(result.rows ?? []).map((r, i) => (
                <tr key={i}>
                  <td>{i + 1}</td>
                  {r.map((c, j) => (
                    <td key={j}>{c}</td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </fieldset>
  )
}
