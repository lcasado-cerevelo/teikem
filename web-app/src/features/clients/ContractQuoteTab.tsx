// Pestaña «Cotizador» de la sección de contratos (F-A3, manual 02 cap. 5.5): cotiza una orden SIN crearla contra el contrato vigente del
// cliente en una fecha (por omisión hoy). Líneas (servicio, paquete, piezas), monto COD y fecha «asOf»; el resultado desglosa cada línea
// (tarifa base, piezas extra, de dónde salió cada monto), el cargo por despacho, el cargo por COD y el total, y avisa qué contrato se usó.
// Permiso contracts.read (el mismo de la sección): son precios de CONTRATO, no de una orden, y el servidor no los oculta por orders.view_price.
import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo, useRef, useState } from 'react'
import { useFieldArray, useForm } from 'react-hook-form'
import { useLookups } from '../../kernel/catalogs'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip, type ChipTone, DataTable, type DataColumn, DateInput, Field, Form, NumberInput, Select, useElementWidth } from '../../kernel/ui'
import { IconTrash } from '../../kernel/ui'
import { useContract } from './contractApi'
import { lookupOptions } from './contractRules'
import { useQuote } from './quoteApi'
import {
  buildQuoteRequest,
  emptyQuote,
  emptyQuoteLine,
  lineSubtotal,
  linesWithoutRate,
  PACKAGE_TYPE_DOMAIN,
  quoteSchema,
  rethrowQuoteError,
  SERVICE_TYPE_DOMAIN,
  sourceOf,
  type QuoteLine,
  type QuoteSource,
  type RateQuote,
} from './quoteRules'
import type { ClientDetail } from './clientRules'

const SOURCE_TONE: Record<QuoteSource, ChipTone> = { CONTRACT: 'deliv', GENERIC: 'cod', NONE: 'warn' }

export function ContractQuoteTab({ client }: { client: ClientDetail }) {
  const t = useT()
  const f = useFormat()
  const clientPublicId = client.publicId ?? ''
  const quote = useQuote()
  const { data: services = [] } = useLookups(SERVICE_TYPE_DOMAIN)
  const { data: packages = [] } = useLookups(PACKAGE_TYPE_DOMAIN)
  const [result, setResult] = useState<RateQuote | null>(null)
  const ref = useRef<HTMLDivElement>(null)
  const width = useElementWidth(ref)

  const schema = useMemo(
    () =>
      quoteSchema({
        serviceRequired: t('clients.rates.errors.serviceRequired'),
        packageRequired: t('clients.rates.errors.packageRequired'),
        piecesMin: t('clients.quote.errors.piecesMin'),
        piecesInt: t('clients.quote.errors.piecesInt'),
        codMin: t('clients.quote.errors.codMin'),
        repeated: t('clients.quote.errors.repeated'),
      }),
    [t],
  )
  const form = useForm({ resolver: zodResolver(schema), defaultValues: emptyQuote() })
  const { fields, append, remove } = useFieldArray({ control: form.control, name: 'lines' })

  // contrato que se usó: su ficha trae la moneda; el resumen de la ficha del cliente trae número, título y estatus
  const { data: usedContract } = useContract(result?.contractPublicId ?? null)
  const summary = (client.contracts ?? []).find((c) => c.publicId === result?.contractPublicId)
  const currency = usedContract?.currency
  const money = (n: number | null | undefined) => (n == null ? '—' : f.money(n, { currency, unitPrice: true }))
  const serviceLabel = (code: string | null | undefined) => services.find((s) => s.code === code)?.label ?? code ?? ''
  const packageLabel = (code: string | null | undefined) => packages.find((p) => p.code === code)?.label ?? code ?? ''
  const sourceLabel = (s: QuoteSource) => t(`clients.quote.source.${s}`)

  const columns: DataColumn<QuoteLine>[] = [
      { id: 'service', header: t('clients.rates.service'), cell: (l) => <b>{serviceLabel(l.serviceType)}</b>, sortValue: (l) => serviceLabel(l.serviceType), card: 'title' },
      { id: 'package', header: t('clients.rates.package'), cell: (l) => packageLabel(l.packageType), sortValue: (l) => packageLabel(l.packageType) },
      { id: 'pieces', header: t('clients.quote.pieces'), align: 'end', cell: (l) => <span className="mono">{l.pieces}</span>, sortValue: (l) => l.pieces },
      {
        id: 'base',
        header: t('clients.quote.base'),
        align: 'end',
        cell: (l) => (
          <span className="cl-quote-amt">
            <span className="mono">{money(l.baseRate)}</span> <Chip tone={SOURCE_TONE[sourceOf(l.baseSource)]}>{sourceLabel(sourceOf(l.baseSource))}</Chip>
          </span>
        ),
        sortValue: (l) => l.baseRate,
      },
      {
        id: 'extra',
        header: t('clients.quote.extra'),
        align: 'end',
        cell: (l) => (
          <span className="cl-quote-amt">
            <span className="mono">{money(l.extraPieces)}</span> <Chip tone={SOURCE_TONE[sourceOf(l.extraSource)]}>{sourceLabel(sourceOf(l.extraSource))}</Chip>
          </span>
        ),
        sortValue: (l) => l.extraPieces,
      },
      { id: 'subtotal', header: t('clients.quote.subtotal'), align: 'end', cell: (l) => <b className="mono">{money(lineSubtotal(l))}</b>, sortValue: (l) => lineSubtotal(l) },
  ]

  const missing = result ? linesWithoutRate(result) : []

  return (
    <div className="cl-ctr-tab" ref={ref}>
      <p className="help cl-ctr-note">{t('clients.quote.help')}</p>
      <Form
        form={form}
        onSubmit={async (v) => {
          setResult(null)
          try {
            setResult(await quote.mutateAsync(buildQuoteRequest(clientPublicId, v)))
          } catch (err) {
            rethrowQuoteError(err)
          }
        }}
      >
        <fieldset className="cl-fs">
          {fields.map((row, i) => (
            <div key={row.id} className="cl-quote-line" role="group" aria-label={t('clients.quote.lineN', { n: i + 1 })}>
              <Field name={`lines.${i}.serviceType`} label={t('clients.rates.service')} required>
                <Select options={lookupOptions(services)} placeholder={t('clients.rates.choose')} />
              </Field>
              <Field name={`lines.${i}.packageType`} label={t('clients.rates.package')} required>
                <Select options={lookupOptions(packages)} placeholder={t('clients.rates.choose')} />
              </Field>
              <Field name={`lines.${i}.pieces`} label={t('clients.quote.pieces')} required>
                <NumberInput min={1} step={1} />
              </Field>
              <div className="cl-quote-rm">
                <button type="button" className="btn sm danger" disabled={fields.length === 1} onClick={() => remove(i)} aria-label={t('clients.quote.removeLine', { n: i + 1 })}>
                  <IconTrash /> {t('clients.quote.remove')}
                </button>
              </div>
            </div>
          ))}
        </fieldset>
        <div className="cl-sec-head cl-quote-add">
          <button type="button" className="btn sm" onClick={() => append(emptyQuoteLine())}>
            {t('clients.quote.addLine')}
          </button>
        </div>
        <div className="r2">
          <Field name="codAmount" label={t('clients.quote.cod')} help={t('clients.quote.codHelp')}>
            <NumberInput min={0} />
          </Field>
          <Field name="asOf" label={t('clients.quote.asOf')} help={t('clients.quote.asOfHelp')}>
            <DateInput />
          </Field>
        </div>
        <div className="form-acts">
          <button
            type="button"
            className="btn"
            disabled={form.formState.isSubmitting}
            onClick={() => {
              form.reset(emptyQuote(), { keepDirtyValues: false })
              setResult(null)
            }}
          >
            {t('clients.quote.clear')}
          </button>
          <button type="submit" className="btn flow" disabled={form.formState.isSubmitting}>
            {form.formState.isSubmitting ? t('common.loading') : t('clients.quote.submit')}
          </button>
        </div>
      </Form>

      {result && (
        <section className="cl-quote-res" aria-label={t('clients.quote.resultTitle')}>
          <h3 className="cl-ctr-h">{t('clients.quote.resultTitle')}</h3>
          <p className="note cl-ctr-note" data-testid="quote-contract">
            {result.contractPublicId
              ? t('clients.quote.usedContract', {
                  contract: summary ? `${summary.contractNumber ?? ''}${summary.title ? ` · ${summary.title}` : ''}` : (usedContract?.contractNumber ?? ''),
                  status: summary?.statusLabel ?? usedContract?.statusLabel ?? result.contractStatus ?? '',
                  date: f.date(result.asOf),
                })
              : t('clients.quote.noContract', { date: f.date(result.asOf) })}
          </p>
          {missing.map((l) => (
            <p key={`${l.serviceType}|${l.packageType}`} className="note cl-ctr-note cl-quote-warn" role="status">
              {t('clients.quote.noRate', { service: serviceLabel(l.serviceType), package: packageLabel(l.packageType) })}
            </p>
          ))}
          <DataTable
            label={t('clients.quote.resultTitle')}
            columns={columns}
            rows={result.lines ?? []}
            rowKey={(l) => `${l.serviceType}|${l.packageType}`}
            pagination={false}
            exportable={false}
            forceCards={width > 0 && width < 640}
          />
          <dl className="cl-quote-sum">
            <div>
              <dt>{t('clients.quote.dispatch')}</dt>
              <dd className="mono">{money(result.dispatchFee)}</dd>
            </div>
            <div>
              <dt>{t('clients.quote.codFee')}</dt>
              <dd className="mono">{money(result.codFee)}</dd>
            </div>
            <div className="cl-quote-total">
              <dt>{t('clients.quote.total')}</dt>
              <dd className="mono">{money(result.total)}</dd>
            </div>
          </dl>
        </section>
      )}
    </div>
  )
}
