import { useEffect, useRef, type ReactNode } from 'react'

interface Props {
  open: boolean
  title: string
  children?: ReactNode
  confirmLabel: string
  danger?: boolean
  busy?: boolean
  onConfirm: () => void
  onCancel: () => void
}

/** Accessible confirmation (PRD: confirmation dialogs for destructive actions). Uses the native <dialog> element. */
export function ConfirmDialog({ open, title, children, confirmLabel, danger, busy, onConfirm, onCancel }: Props) {
  const ref = useRef<HTMLDialogElement>(null)

  useEffect(() => {
    const dialog = ref.current
    if (!dialog) return
    if (open && !dialog.open) dialog.showModal?.()
    if (!open && dialog.open) dialog.close?.()
  }, [open])

  if (!open) return null

  return (
    <dialog ref={ref} className="dialog" aria-labelledby="confirm-title" onCancel={(e) => { e.preventDefault(); onCancel() }} open={open}>
      <h2 id="confirm-title">{title}</h2>
      {children}
      <div className="dialog-actions">
        <button type="button" className="secondary" onClick={onCancel} disabled={busy}>
          Cancel
        </button>
        <button type="button" className={danger ? 'danger' : undefined} onClick={onConfirm} disabled={busy}>
          {busy ? 'Please wait…' : confirmLabel}
        </button>
      </div>
    </dialog>
  )
}
