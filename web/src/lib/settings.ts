export type VolumeUnit = "Cft" | "M3";
export type SurveyRequirement = "Off" | "PumpJobs" | "All";

export interface TenantSettings {
    name: string;
    slug: string;
    volumeUnit: VolumeUnit;
    pumpFreeAboveCft: number | null;
    requireSurveyBeforePlanning: SurveyRequirement;
    pumpRestShifts: number;
    requireCreditApproval: boolean;
    vatOnConcrete: boolean;
    averageSpeedKmh: number;
    pumpAssemblyMinutes: number;
    truckLoadCft: number;
    lateThresholdMinutes: number;
    utcOffsetMinutes: number;
    returnWindowMinutes: number;
    customerOwnershipDays: number;
    timeZoneLocked: boolean;
}

export const surveyLabel: Record<SurveyRequirement, string> = {
    Off: "Never",
    PumpJobs: "Only for pump jobs",
    All: "For every order",
};

// The settings that change what customers pay or who approves an order. Changing one needs a reason.
export const reasonFields = [
    "vatOnConcrete",
    "requireCreditApproval",
    "pumpFreeAboveCft",
] as const satisfies readonly (keyof TenantSettings)[];

// "UTC+06:00" for 360.
export const offsetLabel = (minutes: number) => {
    const sign = minutes < 0 ? "-" : "+";
    const abs = Math.abs(minutes);
    const hours = String(Math.floor(abs / 60)).padStart(2, "0");
    const mins = String(abs % 60).padStart(2, "0");
    return `UTC${sign}${hours}:${mins}`;
};

// The offsets in use around the world, from UTC-12:00 to UTC+14:00.
export const offsets = [
    -720, -660, -600, -570, -540, -480, -420, -360, -300, -240, -210, -180,
    -120, -60, 0, 60, 120, 180, 210, 240, 270, 300, 330, 345, 360, 390, 420,
    480, 525, 540, 570, 600, 630, 660, 720, 765, 780, 840,
];

// Whole numbers in a range, as typed into a text box.
export const wholeIn = (value: string, min: number, max: number) => {
    const n = Number(value);
    return value.trim() !== "" && Number.isInteger(n) && n >= min && n <= max;
};
