export interface SetupProgress {
    steps: { key: string; done: boolean }[];
    done: number;
    total: number;
}

export interface SetupStepInfo {
    title: string;
    hint: string;
    to: string;
    action: string;
}

// What each step means and where to do it. A step the API adds later still shows, by its key.
export const setupSteps: Record<string, SetupStepInfo> = {
    settings: {
        title: "Check the company settings",
        hint: "Time zone, VAT, credit approval and planning rules. The time zone can't change once there are orders.",
        to: "/settings",
        action: "Open settings",
    },
    plant: {
        title: "Add a plant with a shift and a batching plant",
        hint: "Pours are planned on a plant's shifts and batched on its batching plants.",
        to: "/plants",
        action: "Open plants",
    },
    fleet: {
        title: "Add a transit mixer and a driver",
        hint: "Loads go out on mixers. Add pumps and their crews too if you pump concrete.",
        to: "/fleet",
        action: "Open fleet",
    },
    catalogue: {
        title: "Add products and materials",
        hint: "Products are what you sell, such as a concrete grade. Materials go into them.",
        to: "/catalogue",
        action: "Open catalogue",
    },
    recipe: {
        title: "Approve a recipe",
        hint: "A recipe says how much of each material goes into a product. It needs approval before it is used.",
        to: "/pricing/recipes",
        action: "Open recipes",
    },
    costs: {
        title: "Approve material costs",
        hint: "What each material costs you. Margins are worked out from these.",
        to: "/pricing/material-costs",
        action: "Open material costs",
    },
    prices: {
        title: "Approve a selling price",
        hint: "An order can only be taken for a product with an active price.",
        to: "/pricing/prices",
        action: "Open prices",
    },
    "sales-rep": {
        title: "Link a sales rep to a user",
        hint: "Customers and orders belong to a sales rep, who must be someone who signs in.",
        to: "/sales-reps",
        action: "Open sales reps",
    },
    customer: {
        title: "Add a customer with a site",
        hint: "Concrete is ordered by a customer for one of their sites.",
        to: "/customers",
        action: "Open customers",
    },
    people: {
        title: "Bring in your team",
        hint: "Share the join code from the Users screen, then approve people and give them roles.",
        to: "/users",
        action: "Open users",
    },
};

export const stepInfo = (key: string): SetupStepInfo =>
    setupSteps[key] ?? { title: key, hint: "", to: "/", action: "Open" };
