import { useState } from "react";
import { LuPlus } from "react-icons/lu";
import { OpButton as Button } from "@/components/operator/OpButton";
import { Field } from "@/components/orders/Field";
import {
    inputClass,
    nextButtonClass,
    panelClass,
} from "@/components/orders/styles";
import {
    useAssignUser,
    useCompanies,
    useCompanyStatus,
    useCreateCompany,
    useUnassigned,
} from "@/components/platform/usePlatform";
import { Skeleton } from "@/components/ui/skeleton";
import { initialsOf } from "@/lib/format";
import {
    slugFrom,
    type CompanySummary,
    type UnassignedUser,
} from "@/lib/platform";
import { cn } from "@/lib/utils";

const day = new Intl.DateTimeFormat(undefined, {
    day: "numeric",
    month: "short",
    year: "numeric",
});

// PlantFlow's own console: the companies using it, and people who signed in without a company.
// Company data stays with each company; this screen never opens it.
export function Platform() {
    const companies = useCompanies();
    const unassigned = useUnassigned();
    const [creating, setCreating] = useState(false);

    return (
        <div className="space-y-8">
            <header className="flex flex-wrap items-end justify-between gap-3">
                <div>
                    <h1 className="display text-4xl leading-none">Companies</h1>
                    <p className="mt-2 max-w-2xl text-sm text-muted-foreground">
                        Create a company and make its first Admin. After that
                        the company's Admin shares its join code with their
                        staff and approves them; you don't need to.
                    </p>
                </div>
                {!creating && (
                    <Button
                        variant="outline"
                        className="h-11 px-4"
                        onClick={() => setCreating(true)}
                    >
                        <LuPlus aria-hidden className="size-4" />
                        New company
                    </Button>
                )}
            </header>

            {creating && <NewCompany onDone={() => setCreating(false)} />}

            <section className="space-y-3">
                {companies.isLoading && <Skeleton className="h-40 w-full" />}
                {companies.error && (
                    <p role="alert" className="text-sm text-late">
                        {companies.error.message}
                    </p>
                )}
                {companies.data?.length === 0 && (
                    <p className="text-sm text-muted-foreground">
                        No companies yet. Create the first one.
                    </p>
                )}
                {companies.data && companies.data.length > 0 && (
                    <ul className={cn(panelClass, "divide-y")}>
                        {companies.data.map((company) => (
                            <CompanyRow key={company.id} company={company} />
                        ))}
                    </ul>
                )}
            </section>

            <section className="space-y-3">
                <div>
                    <h2 className="text-xl font-semibold">
                        Signed in without a company
                    </h2>
                    <p className="max-w-2xl text-sm text-muted-foreground">
                        People who signed in but haven't entered a join code.
                        Make one the first Admin of a new company, or add them
                        to a company to wait for its Admin's approval.
                    </p>
                </div>
                {unassigned.isLoading && <Skeleton className="h-24 w-full" />}
                {unassigned.error && (
                    <p role="alert" className="text-sm text-late">
                        {unassigned.error.message}
                    </p>
                )}
                {unassigned.data?.length === 0 && (
                    <p className="text-sm text-muted-foreground">
                        Nobody is waiting.
                    </p>
                )}
                {unassigned.data && unassigned.data.length > 0 && (
                    <ul className={cn(panelClass, "divide-y")}>
                        {unassigned.data.map((user) => (
                            <UnassignedRow
                                key={user.id}
                                user={user}
                                companies={(companies.data ?? []).filter(
                                    (c) => c.status === "Active",
                                )}
                            />
                        ))}
                    </ul>
                )}
            </section>
        </div>
    );
}

function NewCompany({ onDone }: { onDone: () => void }) {
    const create = useCreateCompany();
    const [name, setName] = useState("");
    const [slug, setSlug] = useState<string | null>(null);
    const effectiveSlug = slug ?? slugFrom(name);
    const ready = name.trim().length >= 2 && effectiveSlug.length >= 3;

    return (
        <form
            className={cn(panelClass, "space-y-4 p-4 sm:p-5")}
            onSubmit={(e) => {
                e.preventDefault();
                if (ready) {
                    create.mutate(
                        { name: name.trim(), slug: effectiveSlug },
                        { onSuccess: onDone },
                    );
                }
            }}
        >
            <h2 className="text-lg font-semibold">New company</h2>
            <div className="grid gap-4 sm:grid-cols-2">
                <Field label="Name">
                    <input
                        value={name}
                        onChange={(e) => setName(e.target.value)}
                        maxLength={200}
                        placeholder="For example Meghna Ready Mix Ltd"
                        className={inputClass}
                        autoFocus
                    />
                </Field>
                <Field
                    label="Short name"
                    hint="Lowercase letters, digits and hyphens. It can't be reused."
                >
                    <input
                        value={effectiveSlug}
                        onChange={(e) => setSlug(e.target.value.toLowerCase())}
                        maxLength={60}
                        className={cn(inputClass, "font-mono")}
                    />
                </Field>
            </div>
            {create.error && (
                <p role="alert" className="text-sm font-medium text-late">
                    {create.error.message}
                </p>
            )}
            <div className="flex flex-wrap gap-2">
                <Button
                    type="submit"
                    className={cn("h-11 px-5", ready && nextButtonClass)}
                    variant={ready ? "default" : "outline"}
                    disabled={!ready || create.isPending}
                >
                    {create.isPending ? "Creating…" : "Create company"}
                </Button>
                <Button
                    type="button"
                    variant="outline"
                    className="h-11 px-5"
                    onClick={onDone}
                >
                    Cancel
                </Button>
            </div>
        </form>
    );
}

function CompanyRow({ company }: { company: CompanySummary }) {
    const status = useCompanyStatus(company.id);
    const [suspending, setSuspending] = useState(false);
    const [reason, setReason] = useState("");
    const suspended = company.status === "Suspended";

    return (
        <li className="space-y-3 px-4 py-3">
            <div className="flex flex-wrap items-center gap-x-6 gap-y-2">
                <div className="min-w-0 flex-1 basis-56">
                    <p className="flex flex-wrap items-center gap-2 font-medium">
                        {company.name}
                        {suspended && (
                            <span className="rounded-sm bg-late px-2 py-0.5 text-xs font-semibold text-white">
                                Suspended
                            </span>
                        )}
                    </p>
                    <p className="text-sm text-muted-foreground">
                        <span className="font-mono">{company.slug}</span>
                        {company.createdAtUtc &&
                            `, since ${day.format(new Date(company.createdAtUtc))}`}
                    </p>
                </div>
                <dl className="flex gap-6 text-sm">
                    <div>
                        <dt className="text-muted-foreground">People</dt>
                        <dd className="font-semibold tabular-nums">
                            {company.userCount}
                        </dd>
                    </div>
                    <div>
                        <dt className="text-muted-foreground">Waiting</dt>
                        <dd className="font-semibold tabular-nums">
                            {company.pendingCount}
                        </dd>
                    </div>
                    <div>
                        <dt className="text-muted-foreground">Admins</dt>
                        <dd
                            className={cn(
                                "font-semibold tabular-nums",
                                company.adminCount === 0 && "text-late",
                            )}
                        >
                            {company.adminCount === 0
                                ? "None yet"
                                : company.adminCount}
                        </dd>
                    </div>
                </dl>
                {suspended ? (
                    <Button
                        variant="outline"
                        className="h-11 px-4"
                        disabled={status.isPending}
                        onClick={() => status.mutate({ action: "reactivate" })}
                    >
                        Reactivate
                    </Button>
                ) : (
                    !suspending && (
                        <Button
                            variant="outline"
                            tone="danger"
                            className="h-11 px-4"
                            onClick={() => setSuspending(true)}
                        >
                            Suspend
                        </Button>
                    )
                )}
            </div>
            {suspending && !suspended && (
                <form
                    className="flex flex-wrap items-end gap-2 rounded-sm border bg-background/60 p-3"
                    onSubmit={(e) => {
                        e.preventDefault();
                        if (reason.trim()) {
                            status.mutate(
                                { action: "suspend", reason: reason.trim() },
                                {
                                    onSuccess: () => {
                                        setSuspending(false);
                                        setReason("");
                                    },
                                },
                            );
                        }
                    }}
                >
                    <Field
                        label="Reason"
                        hint="Everyone in the company loses access at once. Nothing is deleted."
                        className="min-w-0 flex-1 basis-64"
                    >
                        <input
                            value={reason}
                            onChange={(e) => setReason(e.target.value)}
                            maxLength={500}
                            className={inputClass}
                            autoFocus
                        />
                    </Field>
                    <Button
                        type="submit"
                        tone="danger"
                        className="h-11 px-4"
                        disabled={!reason.trim() || status.isPending}
                    >
                        Suspend company
                    </Button>
                    <Button
                        type="button"
                        variant="outline"
                        className="h-11 px-4"
                        onClick={() => {
                            setSuspending(false);
                            status.reset();
                        }}
                    >
                        Never mind
                    </Button>
                </form>
            )}
            {status.error && (
                <p role="alert" className="text-sm font-medium text-late">
                    {status.error.message}
                </p>
            )}
        </li>
    );
}

function UnassignedRow({
    user,
    companies,
}: {
    user: UnassignedUser;
    companies: CompanySummary[];
}) {
    const assign = useAssignUser(user.id);
    const [tenantId, setTenantId] = useState("");
    const [asAdmin, setAsAdmin] = useState(true);
    const name = user.displayName ?? user.email;

    return (
        <li className="flex flex-wrap items-center gap-x-4 gap-y-3 px-4 py-3">
            <span className="grid size-9 shrink-0 place-items-center rounded-full bg-muted text-xs font-bold">
                {initialsOf(name)}
            </span>
            <span className="min-w-0 flex-1 basis-48">
                <span className="block truncate font-medium">{name}</span>
                <span className="block truncate text-sm text-muted-foreground">
                    {user.displayName ? `${user.email}, ` : ""}signed in{" "}
                    {day.format(new Date(user.createdAtUtc))}
                </span>
            </span>
            <form
                className="flex flex-wrap items-center gap-2"
                onSubmit={(e) => {
                    e.preventDefault();
                    if (tenantId) assign.mutate({ tenantId, asAdmin });
                }}
            >
                <select
                    aria-label={`Company for ${name}`}
                    value={tenantId}
                    onChange={(e) => {
                        setTenantId(e.target.value);
                        assign.reset();
                    }}
                    className={cn(inputClass, "w-56")}
                >
                    <option value="">Choose a company</option>
                    {companies.map((c) => (
                        <option key={c.id} value={c.id}>
                            {c.name}
                        </option>
                    ))}
                </select>
                <label className="flex min-h-11 items-center gap-2 text-sm">
                    <input
                        type="checkbox"
                        className="size-4"
                        checked={asAdmin}
                        onChange={(e) => setAsAdmin(e.target.checked)}
                    />
                    As its Admin
                </label>
                <Button
                    type="submit"
                    className="h-11 px-4"
                    variant="outline"
                    disabled={!tenantId || assign.isPending}
                >
                    Add
                </Button>
            </form>
            {assign.error && (
                <p
                    role="alert"
                    className="w-full text-sm font-medium text-late"
                >
                    {assign.error.message}
                </p>
            )}
        </li>
    );
}
