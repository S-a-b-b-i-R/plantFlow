import { useSiteSurveys } from "@/components/surveys/useSurveys";
import { shortDate } from "@/lib/orders";
import { resultLabel, TAGGABLE_SURVEYS } from "@/lib/surveys";
import { inputClass } from "./styles";

export type SurveyPick = { surveyId: string; requestSurvey: boolean };

const NEW = "new";

// Picks the survey an order relies on: one of its site's last three, or a request for a new one.
// The current tag stays listed even when it is no longer among the last three, so it is not lost by accident.
export function SurveyChoice({
    siteId,
    value,
    onChange,
    disabled,
    id,
}: {
    siteId: string;
    value: SurveyPick;
    onChange: (pick: SurveyPick) => void;
    disabled?: boolean;
    id?: string;
}) {
    const surveys = useSiteSurveys(siteId);
    const all = surveys.data ?? [];
    const recent = all.slice(0, TAGGABLE_SURVEYS);
    const current =
        value.surveyId && !recent.some((s) => s.id === value.surveyId)
            ? all.find((s) => s.id === value.surveyId)
            : undefined;

    const selected = value.requestSurvey ? NEW : value.surveyId;

    return (
        <>
            <select
                id={id}
                value={selected}
                disabled={disabled || surveys.isLoading}
                onChange={(e) =>
                    onChange(
                        e.target.value === NEW
                            ? { surveyId: "", requestSurvey: true }
                            : {
                                  surveyId: e.target.value,
                                  requestSurvey: false,
                              },
                    )
                }
                className={inputClass}
            >
                <option value="">No survey</option>
                <option value={NEW}>Request a new survey</option>
                {recent.map((s) => (
                    <option key={s.id} value={s.id}>
                        {shortDate(s.surveyedOn)}, {resultLabel[s.result]}
                        {s.orderNumber ? ` (${s.orderNumber})` : ""}
                    </option>
                ))}
                {current && (
                    <option value={current.id}>
                        {shortDate(current.surveyedOn)},{" "}
                        {resultLabel[current.result]} (older, current)
                    </option>
                )}
            </select>
            {surveys.isError && (
                <span role="alert" className="mt-1 block text-xs text-late">
                    {surveys.error.message}
                </span>
            )}
        </>
    );
}
