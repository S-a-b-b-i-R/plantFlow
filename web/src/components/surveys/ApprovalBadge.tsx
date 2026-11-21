import { can, PERMISSIONS } from "@/auth/permissions";
import { useMe } from "@/auth/useMe";
import { useSurveyApprovals } from "./useSurveys";

// How many surveys wait for this approver, shown next to Surveys in the menu. Nothing when none wait.
export function ApprovalBadge() {
    const me = useMe().data!;
    const approvals = useSurveyApprovals(
        can(me, PERMISSIONS.siteSurveysApprove),
    );
    const count = approvals.data?.surveys.length ?? 0;
    if (count === 0) return null;

    return (
        <span
            className="grid h-5 min-w-5 place-items-center rounded-full bg-signal px-1.5 text-xs font-bold text-ink"
            aria-label={`${count} to approve`}
        >
            {count}
        </span>
    );
}
